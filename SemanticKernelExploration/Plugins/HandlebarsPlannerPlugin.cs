using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;

namespace SemanticKernelExploration.Plugins;

public class HandlebarsPlannerPlugin
{
    [KernelFunction]
    [Description("Creates a handlebars plan to achieve a goal.")]
    [return: Description("A handlebars plan to achieve a goal")]
#pragma warning disable SKEXP0060
    public async Task<HandlebarsPlan> CreatePlan(
        Kernel kernel,
        [Description("The goal to be achieved for which a plan is to be created")] string goal)
    {
        var executionSettings = new OpenAIPromptExecutionSettings()
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.0,
            TopP = 0.1
        };


        var planner = new HandlebarsPlanner(new HandlebarsPlannerOptions()
        {
            AllowLoops = true,
            ExecutionSettings = executionSettings
        });

        var plan = await planner.CreatePlanAsync(kernel, goal);

        return plan;
    }

    [KernelFunction]
    [Description("Executes a handlebars plan.")]
    [return: Description("The result of the executed plan")]
    public async Task<string> ExecutePlan(
        Kernel kernel,
        [Description("The handlebars plan to be executed")] HandlebarsPlan plan)
    {
        return await plan.InvokeAsync(kernel);
    }
#pragma warning restore SKEXP0060
}