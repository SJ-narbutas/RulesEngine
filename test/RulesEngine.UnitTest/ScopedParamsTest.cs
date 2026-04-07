// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using RulesEngine.HelperFunctions;
using RulesEngine.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static RulesEngine.UnitTest.ScopedParamsTest;

namespace RulesEngine.UnitTest
{
    [ExcludeFromCodeCoverage]
    public class MyObject
    {
        public string Name { get; set; }

        public int Count { get; set; }
    }

    [ExcludeFromCodeCoverage]
    public class ScopedParamsTest
    {
        [Theory]
        [InlineData("NoLocalAndGlobalParams")]
        [InlineData("LocalParamsOnly")]
        [InlineData("GlobalParamsOnly")]
        [InlineData("GlobalAndLocalParams")]
        [InlineData("GlobalParamReferencedInLocalParams")]
        [InlineData("GlobalParamReferencedInNextGlobalParams")]
        [InlineData("LocalParamReferencedInNextLocalParams")]
        [InlineData("GlobalParamAndLocalParamsInNestedRules")]
        public async Task BasicWorkflows_ReturnsTrue(string workflowName)
        {
            var workflow = GetWorkflowList();

            var engine = new RulesEngine();
            engine.AddWorkflow(workflow);

            var input1 = new {
                trueValue = true,
                falseValue = false
            };

            var result = await engine.ExecuteAllRulesAsync(workflowName, input1);
            Assert.True(result.All(c => c.IsSuccess));

            CheckResultTreeContainsAllInputs(workflowName, result);

        }

        [Theory]
        [InlineData("GlobalAndLocalParams")]
        public async Task WorkflowUpdate_GlobalParam_ShouldReflect(string workflowName)
        {
            var workflow = GetWorkflowList();

            var engine = new RulesEngine();
            engine.AddWorkflow(workflow);

            var input1 = new {
                trueValue = true,
                falseValue = false
            };

            var result = await engine.ExecuteAllRulesAsync(workflowName, input1);
            Assert.True(result.All(c => c.IsSuccess));

            var workflowToUpdate = workflow.Single(c => c.WorkflowName == workflowName);
            engine.RemoveWorkflow(workflowName);
            workflowToUpdate.GlobalParams.First().Expression = "true == false";
            engine.AddWorkflow(workflowToUpdate);

            var result2 = await engine.ExecuteAllRulesAsync(workflowName, input1);

            Assert.True(result2.All(c => c.IsSuccess == false));
        }


        [Theory]
        [InlineData("GlobalParamsOnly", new[] { false })]
        [InlineData("LocalParamsOnly", new[] { false, true })]
        [InlineData("GlobalAndLocalParams", new[] { false })]
        public async Task DisabledScopedParam_ShouldReflect(string workflowName, bool[] outputs)
        {
            var workflow = GetWorkflowList();

            var engine = new RulesEngine(new string[] { }, new ReSettings {
                EnableScopedParams = false
            });
            engine.AddWorkflow(workflow);

            var input1 = new {
                trueValue = true,
                falseValue = false
            };

            var result = await engine.ExecuteAllRulesAsync(workflowName, input1);
            for (var i = 0; i < result.Count; i++)
            {
                Assert.Equal(result[i].IsSuccess, outputs[i]);
                if (result[i].IsSuccess == false)
                {
                    Assert.StartsWith("Exception while parsing expression", result[i].ExceptionMessage);
                }
            }
        }

        [Theory]
        [InlineData("GlobalParamsOnly")]
        [InlineData("LocalParamsOnly2")]
        [InlineData("GlobalParamsOnlyWithComplexInput")]
        public async Task ErrorInScopedParam_ShouldAppearAsErrorMessage(string workflowName)
        {
            var workflow = GetWorkflowList();

            var engine = new RulesEngine(new string[] { }, null);
            engine.AddWorkflow(workflow);

            var input = new { };
            var result = await engine.ExecuteAllRulesAsync(workflowName, input);

            Assert.All(result, c => {
                Assert.False(c.IsSuccess);
                Assert.StartsWith("Error while compiling rule", c.ExceptionMessage);
            });

        }

        [Theory]
        [InlineData("GlobalParamsOnlyWithComplexInput")]
        [InlineData("LocalParamsOnlyWithComplexInput")]
        public async Task RuntimeErrorInScopedParam_ShouldAppearAsErrorMessage(string workflowName)
        {
            var workflow = GetWorkflowList();

            var engine = new RulesEngine(new string[] { }, null);
            engine.AddWorkflow(workflow);



            var input = new RuleTestClass();
            var result = await engine.ExecuteAllRulesAsync(workflowName, input);

            Assert.All(result, c => {
                Assert.False(c.IsSuccess);
                Assert.StartsWith("Error while executing scoped params for rule", c.ExceptionMessage);
            });


        }

        [Theory]
        [InlineData("LocalParam_CorrectAnswer")]
        public async Task LocalParam_GivesCorrectAnswer(string workflowName)
        {
            var workflow = GetWorkflowList();

            var reSettingsWithCustomTypes = new ReSettings { CustomTypes = new Type[] { } };
            var bre = new RulesEngine(workflow, reSettingsWithCustomTypes);

            var myObject = new MyObject() {
                Name = "My Object",
                Count = 2
            };

            var rp1 = new RuleParameter("myObj", myObject);

            List<RuleResultTree> resultList = await bre.ExecuteAllRulesAsync(workflowName, rp1);
            Assert.True(resultList[0].IsSuccess);

            myObject.Count = 3;

            resultList = await bre.ExecuteAllRulesAsync(workflowName, rp1);
            Assert.False(resultList[0].IsSuccess);

        }



        private void CheckResultTreeContainsAllInputs(string workflowName, List<RuleResultTree> result)
        {
            var workflow = GetWorkflowList().Single(c => c.WorkflowName == workflowName);
            var expectedInputs = new List<string>() { "input1" };
            expectedInputs.AddRange(workflow.GlobalParams?.Select(c => c.Name) ?? new List<string>());


            foreach (var resultTree in result)
            {
                CheckInputs(expectedInputs, resultTree);
            }

        }

        private static void CheckInputs(IEnumerable<string> expectedInputs, RuleResultTree resultTree)
        {
            Assert.All(expectedInputs, input => Assert.True(resultTree.Inputs.ContainsKey(input)));

            var localParamNames = resultTree.Rule.LocalParams?.Select(c => c.Name) ?? new List<string>();
            Assert.All(localParamNames, input => Assert.True(resultTree.Inputs.ContainsKey(input)));

            if (resultTree.ChildResults?.Any() == true)
            {
                foreach (var childResultTree in resultTree.ChildResults)
                {
                    CheckInputs(expectedInputs.Concat(localParamNames), childResultTree);
                }

            }

        }
        private Workflow[] GetWorkflowList()
        {
            return new Workflow[] {
                new Workflow {
                    WorkflowName = "NoLocalAndGlobalParams",
                    Rules = new List<Rule> {
                        new Rule {
                            RuleName = "TruthTest",
                            Expression = "input1.trueValue"
                        }
                    }
                },
                new Workflow {
                    WorkflowName = "LocalParamsOnly",
                    Rules = new List<Rule> {
                        new Rule {

                            RuleName = "WithLocalParam",
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "input1.trueValue"
                                }
                            },
                            Expression = "localParam1 == true"
                        },
                        new Rule {

                            RuleName = "WithoutLocalParam",
                            Expression = "input1.falseValue == false"
                        },
                    }
                },
                new Workflow {
                    WorkflowName = "LocalParamsOnly2",
                    Rules = new List<Rule> {
                        new Rule {

                            RuleName = "WithLocalParam",
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "input1.trueValue"
                                }
                            },
                            Expression = "localParam1 == true"
                        }
                    }
                },

                new Workflow {
                    WorkflowName = "GlobalParamsOnly",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = "input1.falseValue == false"
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {
                            RuleName = "TrueTest",
                            Expression = "globalParam1 == true"
                        }
                    }
                },
                new Workflow {
                    WorkflowName = "GlobalAndLocalParams",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = "input1.falseValue == false"
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {
                            RuleName = "WithLocalParam",
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "input1.trueValue"
                                }
                            },
                            Expression = "globalParam1 == true && localParam1 == true"
                        },
                    }

                },
                new Workflow {
                    WorkflowName = "GlobalParamReferencedInLocalParams",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = "\"testString\""
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {

                            RuleName = "WithLocalParam",
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "globalParam1.ToUpper()"
                                }
                            },
                            Expression = "globalParam1 == \"testString\" && localParam1 == \"TESTSTRING\""
                        },
                    }
                },
                new Workflow {
                    WorkflowName = "GlobalParamReferencedInNextGlobalParams",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = "\"testString\""
                        },
                        new ScopedParam {
                            Name = "globalParam2",
                            Expression = "globalParam1.ToUpper()"
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {
                            RuleName = "WithLocalParam",
                            Expression = "globalParam1 == \"testString\" && globalParam2 == \"TESTSTRING\""
                        },
                    }
                },
                new Workflow {
                    WorkflowName = "LocalParamReferencedInNextLocalParams",
                    Rules = new List<Rule> {
                        new Rule {
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "\"testString\""
                                },
                                new ScopedParam {
                                    Name = "localParam2",
                                    Expression = "localParam1.ToUpper()"
                                }
                            },
                            RuleName = "WithLocalParam",
                            Expression = "localParam1 == \"testString\" && localParam2 == \"TESTSTRING\""
                        },
                    }
                },
                new Workflow {
                    WorkflowName = "GlobalParamAndLocalParamsInNestedRules",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = @"""hello"""
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {
                           RuleName = "NestedRuleTest",
                           Operator = "And",
                           LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = @"""world"""
                                }
                           },
                           Rules =  new List<Rule>{
                               new Rule{
                                   RuleName = "NestedRule1",
                                   Expression = "globalParam1 == \"hello\" && localParam1 == \"world\""
                               },
                               new Rule {
                                   RuleName = "NestedRule2",
                                   LocalParams = new List<ScopedParam> {
                                       new ScopedParam {
                                           Name = "nestedLocalParam1",
                                           Expression = "globalParam1 + \" \" + localParam1"
                                       }
                                   },
                                   Expression = "nestedLocalParam1 == \"hello world\""
                               }

                           }

                        }
                    }
                },
                new Workflow {
                    WorkflowName = "LocalParamsOnlyWithComplexInput",
                    Rules = new List<Rule> {
                        new Rule {

                            RuleName = "WithLocalParam",
                            LocalParams = new List<ScopedParam> {
                                new ScopedParam {
                                    Name = "localParam1",
                                    Expression = "input1.Country.ToLower()"
                                }
                            },
                            Expression = "localParam1 == \"hello\""
                        }
                    }
                },
                new Workflow {
                    WorkflowName = "GlobalParamsOnlyWithComplexInput",
                    GlobalParams = new List<ScopedParam> {
                        new ScopedParam {
                            Name = "globalParam1",
                            Expression = "input1.Country.ToLower()"
                        }
                    },
                    Rules = new List<Rule> {
                        new Rule {
                            RuleName = "TrueTest",
                            Expression = "globalParam1 == \"hello\""
                        },
                        new Rule {
                            RuleName = "TrueTest2",
                            Expression = "globalParam1.ToUpper() == \"HELLO\""
                        }
                    }
                },
                new Workflow {
                    WorkflowName = "LocalParam_CorrectAnswer",
                    Rules = new List<Rule> {
                        new Rule
                        {
                            RuleName = "Test Rule",
                            LocalParams = new List<LocalParam>
                            {
                                new LocalParam
                                {
                                    Name = "threshold",
                                    Expression = "3"
                                },
                                new LocalParam
                                {
                                    Name = "myList",
                                    Expression = "new int[]{ 1, 2, 3, 4, 5 }"
                                }
                            },
                            SuccessEvent = "Count is within tolerance.",
                            ErrorMessage = "Not as expected.",
                            Expression = "myList.Where(x => x < threshold).Contains(myObj.Count)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression
                        }
                    }
                }
            };
        }


        [Fact]
        public async Task ErrorInScopedParam_ShouldAppearAsErrorMessage2()
        {
            // Arrange
            var axLoad = new AX_Load { ItemName = "G1A_01", Width = 501.0, Depth = 600.0, Height = 300.00, Weight = 26.5, Qty = 4 };
            var config = new StockPalletConfig { PalletSize = new Size(1200, 800), ItemsPerLayer = 4, LayersPerPallet = 3 };
            var map = new StockRuleResult { RuleType = RuleTypeEnum.Stock, AllowedPallets = [new Size(1200, 800), new Size(1200, 1200)], Config = config };

            var workflow = GetCustomWorkflows();

            var engine = new RulesEngine(new string[] { }, Settings);
            engine.AddWorkflow(workflow);

            var inputs = new List<RuleParameter> {
                new("axLoad", axLoad),
                new("ruleResult", map),
            }.ToArray();
            var result = await engine.ExecuteAllRulesAsync("OverrideRules", inputs, CancellationToken.None);

            Assert.All(result, c => {
                Assert.False(c.IsSuccess);
                Assert.StartsWith("Error while compiling rule", c.ExceptionMessage);
            });
        }

        public static ReSettings Settings => new() {
            // To use regex in RulesEngine, needs to be registered as CustomType
            CustomTypes = [typeof(Regex), typeof(RegexOptions), typeof(Utils), typeof(ActionInfo), typeof(RuleTypeEnum), typeof(StockPalletConfig), typeof(Size), typeof(AX_Load), typeof(StockRuleResult)],
            //CustomActions = new Dictionary<string, Func<RulesEngine.Actions.ActionBase>> {
            //        { StockRuleResultContextAction.CONTEXT_ACTION_NAME, () => new StockRuleResultContextAction() },
            //    },
            IsExpressionCaseSensitive = false,
            EnableExceptionAsErrorMessage = true,
            EnableFormattedErrorMessage = true,
            EnableScopedParams = true,
            UseFastExpressionCompiler = true,
        };

        private Workflow[] GetCustomWorkflows()
        {
            #region create workflow rules
            var workflow = new Workflow {
                WorkflowName = "OverrideRules",
                RuleExpressionType = RuleExpressionType.LambdaExpression,
                Rules = new List<Rule> {
                        new() {
                            RuleName = "Visi gaminiai telpa ant STOCK pallet.",
                            Expression = "(IsCabins OR IsChairs) && ruleResult.RuleType == \"Stock\" && axLoad.Qty > 0 && (axLoad.Qty % capacity == 0)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions = new RuleActions {
                                OnSuccess = new ActionInfo {
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Stock }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Stock}",
                        },
                        new() {
                            RuleName = "Kai spintu svoris  < 60kg",
                            Expression = "IsCabins && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight < 60 && Utils.IsWithin(axLoad.Qty, 1, capacity - 1)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai spintu svoris >= 60kg ir Nesurenkamas reikiamas kiekis",
                            Expression = "IsCabins && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight >= 60 && Utils.IsWithin(axLoad.Qty, 1, capacity - 1)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai spintu svoris >= 60kg ir Aukstis iki 1750",
                            Expression = "IsCabins && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight >= 60 && Utils.IsWithin(axLoad.H, 300, 1750)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai didelis kiekis ir uzpildymas >= 0.5",
                            Expression = "(IsCabins OR IsChairs) && axLoad.Qty > capacity && ruleResult.RuleType == \"Stock\" && reminder >= 0.5",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai kedziu svoris  < 60kg",
                            Expression = "IsChairs && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight < 60 && Utils.IsWithin(axLoad.Qty, itemsPerLayer + 1, capacity - 1)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai kedziu svoris >= 60kg ir Nesurenkamas reikiamas kiekis",
                            Expression = "IsChairs && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight >= 60 && Utils.IsWithin(axLoad.Qty, 1, capacity - 1)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                        },
                        new() {
                            RuleName = "Kai kedziu svoris >= 60kg ir Aukstis iki 1750",
                            Expression = "IsChairs && axLoad.Qty > 0 && ruleResult.RuleType == \"Stock\" && axLoad.Weight >= 60 && Utils.IsWithin(axLoad.H, 300, 1750)",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions =  new RuleActions {
                                OnSuccess = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", RuleTypeEnum.Both }
                                    }
                                },
                                OnFailure = new ActionInfo{
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {
                                        { "Expression", "ruleResult.RuleType" }
                                    }
                                }
                            },
                            SuccessEvent = $"{RuleTypeEnum.Both}",
                       },
                    },
                GlobalParams = new List<ScopedParam> {
                        new() {Name = "isNoStandard", Expression = "!string.IsNullOrEmpty(axLoad.ItemName) AND Regex.IsMatch(axLoad.ItemName, \"^[nN]\\\\w+[gG]\\\\w*\") == true" },
                        new() {Name = "isNoStandardChair",  Expression = "!string.IsNullOrEmpty(axLoad.Descr) AND Regex.IsMatch(axLoad.Descr, \"\\\\bk\u0117d|\\\\bked|\\\\bkoj|\\\\br\u0117mas|\\\\bpufas\", RegexOptions.IgnoreCase) == true" },
                        new() {Name = "isNoStandardCabins", Expression = "!string.IsNullOrEmpty(axLoad.Descr) AND Regex.IsMatch(axLoad.Descr, \"\\\\bspint|\\\\blocker|\\\\bloker|\\\\bkub|\\\\bbok\u0161t\", RegexOptions.IgnoreCase) == true" },
                        new() {Name = "IsChairs", Expression = "Utils.CheckStartsWith(axLoad.ItemName, \"G5\") OR (isNoStandard AND isNoStandardChair)" },
                        new() {Name = "IsCabins", Expression = "Utils.CheckStartsWith(axLoad.ItemName, \"G1A,G1B,G1S,G4O\") OR (isNoStandard AND isNoStandardCabins)" },
                        new() {Name = "itemsPerLayer", Expression = "ruleResult.Config.ItemsPerLayer" },
                        new() {Name = "layersPerPallet", Expression = "ruleResult.Config.LayersPerPallet" },
                        new() {Name = "capacity", Expression = "itemsPerLayer * layersPerPallet" },
                        new() {Name = "reminder", Expression = "(1.0 * axLoad.Qty) / capacity % 1" },
                    }
            };
            #endregion

            return [workflow];
        }

        [DebuggerDisplay("Item={ItemName} Qty={Qty} Size:[{L}x{W}x{H}] Descr={Descr}")]
        public sealed class AX_Load
        {
            public string LoadId { get; set; } = default!;
            public string SoId { get; set; } = default!;
            public string? So { get; set; } = default;
            public string? AssemblyId { get; set; }
            public string ItemName { get; set; } = default!;
            public string SerialNo { get; set; } = default!;
            public int Qty { get; set; }
            [Newtonsoft.Json.JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public double Depth { get; set; }
            [Newtonsoft.Json.JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public double Width { get; set; }
            [Newtonsoft.Json.JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public double Height { private get; set; }
            public double Weight { get; set; }
            public string Descr { get; set; } = default!;
            public string StackGroup { get; set; } = default!;
            public double W => Depth > Width ? Width : Depth;
            public double L => Depth >= Width ? Depth : Width;
            public double H => Height;
            [Newtonsoft.Json.JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public bool IsException { get; set; } = false;
            public int MaxItemsInStack { get; set; }
            public bool IsHaveAssemblyId => !string.IsNullOrWhiteSpace(AssemblyId);

            public bool NeedsBoxing { get; set; } = false;

            public string TransportId { get; set; } = "11";

            public int? MaxWeight { get; set; }
            public int? MaxChairH { get; set; }
            public int? MaxHeight { get; set; }
            public int? MaxHeight2 { get; set; }
            public string? ProductId { get; set; }
            public string? Additional { get; set; }
            public double VolumeMm3 => L * W * H;
            public double VolumeM3 => L * W * H / (1000 * 1000 * 1000);
            public bool IsAllowMixing { get; set; } = true;
            public bool AvoidSplitPackages { get; set; }
            public bool DoNotStackPallets { get; set; }

            public bool IsFitToBox(double l, double w, double h)
            {
                if (l < 0 || w < 0 || h < 0)
                    return false;
                if (L > l)
                    return false;
                if (W > w)
                    return false;
                if (H > h)
                    return false;
                return true;
            }

            ///// <summary>
            ///// Is the item fit to (default) carton? CARTON_580x370x100
            ///// </summary>
            ///// <returns></returns>
            //public bool IsFitToCarton(CartonDto carton)
            //{
            //    if (carton.Length == null || carton.Width == null || carton.Height == null)
            //        return false;
            //    return IsFitToBox(carton.Length.Value, carton.Width.Value, carton.Height.Value);
            //}

            ///// <summary>
            ///// override NeedsBoxing if the item dont fit to default carton. Else return NeedsBoxing.
            ///// </summary>
            ///// <returns></returns>
            //public bool IsNeedsBoxing(CartonDto carton)
            //{
            //    if (!IsFitToCarton(carton))
            //        NeedsBoxing = false;

            //    return NeedsBoxing;
            //}

            public AX_Load Copy() => ShallowCopy();

            public AX_Load ShallowCopy() => (AX_Load)MemberwiseClone();

            public AX_Load DeepCopy()
            {
                var other = (AX_Load)MemberwiseClone();
                return other;
            }

            public string ToJson()
            {
                //var options = new JsonSerializerOptions {
                //    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                //    WriteIndented = true,
                //    AllowTrailingCommas = true,
                //    PropertyNameCaseInsensitive = true
                //};
                //return Newtonsoft.Json.JsonSerializer.Serialize(this, options);
                return System.Text.Json.JsonSerializer.Serialize(this, RulesInputParameterJsonSourceContext.Default.AX_Load);
            }

            [Newtonsoft.Json.JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public StockRuleResult? StockRuleResult { get; set; } = StockRuleResult.None;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<RuleTypeEnum>))]
        [DataContract]
        public enum RuleTypeEnum
        {
            [DataMember(Name = "None")]
            [JsonPropertyName("None")]
            None,
            [JsonPropertyName("Stock")]
            [DataMember(Name = "Stock")]
            Stock,
            [JsonPropertyName("Allowed")]
            [DataMember(Name = "Allowed")]
            Allowed,
            [JsonPropertyName("Both")]
            [DataMember(Name = "Both")]
            Both,
        }

        //[JsonSerializable(typeof(StockRuleResult))]
        //[JsonSerializable(typeof(object))]
        [DataContract]
        public record StockRuleResult
        {
            [JsonConverter(typeof(JsonStringEnumConverter<RuleTypeEnum>))]
            [DataMember(Name = "RuleType")]
            [JsonPropertyName("RuleType")]
            public RuleTypeEnum RuleType { get; set; }

            [DataMember(Name = "Config")]
            [JsonPropertyName("Config")]
            public StockPalletConfig? Config { get; set; } = null;

            [DataMember(Name = "AllowedPallets")]
            [JsonPropertyName("AllowedPallets")]
            public Size[]? AllowedPallets { get; set; } = null;

            [DataMember(Name = "RuleName")]
            [JsonPropertyName("RuleName")]
            public string? RuleName { get; set; }

            [JsonIgnore]
            public bool IsUseStockPallet => Config != null;

            [JsonIgnore]
            public bool IsAllowedPallet => AllowedPallets != null;

            public static StockRuleResult None => FromTuple((RuleTypeEnum.None, null, null));
            public static StockRuleResult FromTuple((RuleTypeEnum RuleType, StockPalletConfig? Config, Size[]? AllowedPallets) tuple)
            {
                return new StockRuleResult { RuleType = tuple.RuleType, Config = tuple.Config, AllowedPallets = tuple.AllowedPallets };
            }
        }

        [DataContract]
        public struct Size(int l, int w)
        {
            [DataMember(Name = "L")]
            [JsonPropertyName("L")]
            public int L { get; set; } = l;
            [DataMember(Name = "W")]
            [JsonPropertyName("W")]
            public int W { get; set; } = w;

            public override readonly string ToString() => $"{L}x{W}";
            public override readonly bool Equals(object? obj)
            {
                if (obj == null)
                    return false;

                if (obj is not Size)
                    return false;

                Size other = (Size)obj;
                // select the fields you want to compare between the 2 objects
                return L == other.L && W == other.W;
            }

            public static bool operator ==(Size left, Size right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Size left, Size right)
            {
                return !(left == right);
            }

            public override int GetHashCode() => HashCode.Combine(L, W);
        }

        [DataContract]
        public struct StockPalletConfig
        {
            [DataMember(Name = "PalletSize")]
            [JsonPropertyName("PalletSize")]
            public Size PalletSize { get; set; }

            [DataMember(Name = "ItemsPerLayer")]
            [JsonPropertyName("ItemsPerLayer")]
            [Newtonsoft.Json.JsonProperty("ItemsPerLayer")]
            public int ItemsPerLayer { get; set; }

            [DataMember(Name = "LayersPerPallet")]
            [JsonPropertyName("LayersPerPallet")]
            public int LayersPerPallet { get; set; }

            public override string ToString() => $"{PalletSize.L}x{PalletSize.W} {ItemsPerLayer} {LayersPerPallet}";
        }
    }

    public record RuleInputParameter
    {
        public required string InputRuleName { get; set; }
        public required object Parameters { get; set; }
    }

    public static class Utils
    {
        /// <summary>
        /// "Expression": "Utils.CheckContains(input1.country, \"india,usa,canada,France\") == true"
        /// </summary>
        /// <param name="check"></param>
        /// <param name="valList"></param>
        /// <returns></returns>
        public static bool CheckContains(string check, string valList)
        {
            if (string.IsNullOrEmpty(check) || string.IsNullOrEmpty(valList))
                return false;

            var list = valList.Split(',').ToList();
            return list.Contains(check);
        }

        /// <summary>
        /// "Expression": "Utils.CheckStartsWith(axLoad.ItemName, \"G1A,G1B,G1S\") == true"
        /// </summary>
        /// <param name="check"></param>
        /// <param name="valList"></param>
        /// <returns></returns>
        public static bool CheckStartsWith(string check, string valList)
        {
            if (string.IsNullOrEmpty(check) || string.IsNullOrEmpty(valList))
                return false;

            var list = valList.Split(',').ToList();
            return list.Any(check.StartsWith);
        }

        public static int[] IntArray(params int[] values) => values;

        public static long[] LongArray(params long[] values) => values;

        public static float[] FloatArray(params float[] values) => values;

        public static double[] DoubleArray(params double[] values) => values;

        public static decimal[] DecimalArray(params decimal[] values) => values;

        public static string[] StringArray(params string[] values) => values;

        public static bool[] BoolArray(params bool[] values) => values;

        public static char[] CharArray(params char[] values) => values;

        public static DateTime[] DateTimeArray(params string[] values) => [.. values.Select(DateTime.Parse)];

        public static Guid[] GuidArray(params string[] values) => [.. values.Select(Guid.Parse)];

        public static bool IsWithin(int value, int minimum, int maximum) => value >= minimum && value <= maximum;
        public static bool IsIntWithin(int value, int minimum, int maximum) => value >= minimum && value <= maximum;
        public static bool IsWithin(double value, int minimum, int maximum) => value >= minimum && value <= maximum;
        public static bool IsDoubleWithin(double value, int minimum, int maximum) => value >= minimum && value <= maximum;
        public static bool IsWithin(long value, long minimum, long maximum) => value >= minimum && value <= maximum;
        public static bool IsLongWithin(long value, int minimum, int maximum) => value >= minimum && value <= maximum;
    }

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenReading,
        GenerationMode = JsonSourceGenerationMode.Default,
        ReadCommentHandling = JsonCommentHandling.Skip,
        ReferenceHandler = JsonKnownReferenceHandler.IgnoreCycles,
        UseStringEnumConverter = true
        )]
    [JsonSerializable(typeof(StockRuleResult))]
    [JsonSerializable(typeof(Dictionary<string, StockRuleResult>))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(StockPalletConfig))]
    [JsonSerializable(typeof(Size))]
    [JsonSerializable(typeof(Size[]))]
    [JsonSerializable(typeof(AX_Load))]
    //[JsonSerializable(typeof(LoadsListMetrics))]
    [JsonSerializable(typeof(RuleInputParameter))]
    [JsonSerializable(typeof(List<RuleInputParameter>))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(object))]
    public partial class RulesInputParameterJsonSourceContext : JsonSerializerContext
    {
        // Removed manually implemented members.
    }
}
