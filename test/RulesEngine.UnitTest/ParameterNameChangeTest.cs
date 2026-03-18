// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using RulesEngine.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static RulesEngine.UnitTest.RulesEngineTest;

namespace RulesEngine.UnitTest
{
    [ExcludeFromCodeCoverage]
    public class ParameterNameChangeTest
    {
        [Fact]
        public async Task RunTwiceTest_ReturnsExpectedResults()
        {
            var workflow = new Workflow {
                WorkflowName = "ParameterNameChangeWorkflow",
                Rules = new Rule[] {
                    new Rule {
                        RuleName = "ParameterNameChangeRule",
                        RuleExpressionType = RuleExpressionType.LambdaExpression,
                        Expression = "test.blah == 1"
                    }
                }
            };
            var engine = new RulesEngine();
            engine.AddOrUpdateWorkflow(workflow);

            dynamic dynamicBlah = new ExpandoObject();
            dynamicBlah.blah = (Int64)1;
            var input_pass = new RuleParameter("test", dynamicBlah);
            var input_fail = new RuleParameter("SOME_OTHER_NAME", dynamicBlah);
            // RuleParameter name matches expression, so should pass.
            var pass_results = await engine.ExecuteAllRulesAsync("ParameterNameChangeWorkflow", input_pass);
            // RuleParameter name DOES NOT MATCH expression, so should fail.
            var fail_results = await engine.ExecuteAllRulesAsync("ParameterNameChangeWorkflow", input_fail);
            Assert.True(pass_results.First().IsSuccess);
            Assert.False(fail_results.First().IsSuccess);
        }

        [Fact]
        public async Task AnotherParameterName_ReturnsExpectedResults()
        {
            var workflow = new Workflow() {
                WorkflowName = "ParameterNameChangeWorkflow",
                Rules = new List<Rule> {
                        new() {
                            RuleName = "ParameterNameChangeRule",
                            Expression = "IsItem AND testParam.Qty > 0",
                            RuleExpressionType = RuleExpressionType.LambdaExpression,
                            Actions = new RuleActions {
                                OnSuccess = new ActionInfo {
                                    Name = "OutputExpression",
                                    Context = new Dictionary<string, object> {{"expression", "testParam.Qty" } }
                                }
                            }
                        },
                    },
                GlobalParams = new List<ScopedParam> {
                        new() {Name = "isTest", Expression ="Regex.IsMatch(testParam.Item, \"^[tT]\\\\w+[tT]\\\\w*\") == true" },
                        new() {Name = "isTestOrItem",  Expression ="Regex.IsMatch(testParam.Description, \"\\\\btest|\\\\bitem\", RegexOptions.IgnoreCase) == true" },
                        new() {Name = "IsItem", Expression ="utils.CheckExists(String(testParam.Item)) == true OR (isTest AND isTestOrItem)" },
                    }
            };

            var reSettings = new ReSettings {
                CustomTypes = [
                    typeof(System.Text.RegularExpressions.Regex),
                    typeof(System.Text.RegularExpressions.RegexOptions),
                    typeof(TestInstanceUtils),
                    ]
            };
            var engine = new RulesEngine(reSettings);
            engine.AddOrUpdateWorkflow(workflow);

            dynamic dynamicTestParam = new ExpandoObject();
            dynamicTestParam.Qty = 1;
            dynamicTestParam.Item = "Test";
            dynamicTestParam.Description = "Test Item";

            var input_pass = new RuleParameter("testParam", dynamicTestParam);
            var input_fail = new RuleParameter("SOME_OTHER_NAME", dynamicTestParam);
            var utils = new RuleParameter("utils", new TestInstanceUtils());
            var inputParams = new List<RuleParameter> { input_pass, utils };
            // RuleParameter name matches expression, so should pass.
            var pass_results = await engine.ExecuteAllRulesAsync("ParameterNameChangeWorkflow", [.. inputParams]);
            // RuleParameter name DOES NOT MATCH expression, so should fail.
            var fail_results = await engine.ExecuteAllRulesAsync("ParameterNameChangeWorkflow", [input_fail, utils]);
            Assert.True(pass_results.First().IsSuccess);
            Assert.False(fail_results.First().IsSuccess);
        }
    }
}
