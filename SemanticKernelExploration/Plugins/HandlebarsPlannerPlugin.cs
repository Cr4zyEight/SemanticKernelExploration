using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;

namespace SemanticKernelExploration.Plugins;

public class HandlebarsPlannerPlugin
{
    [KernelFunction]
    [Description("Creates a plan to achieve a goal.")]
#pragma warning disable SKEXP0060
    public async Task<string> CreatePlan(
        Kernel kernel,
        [Description("The goal to be achieved for which a plan is to be created")] string goal, 
        [Description("required parameters to fulfill request")] KernelArguments requiredVariables)
    {
        Console.WriteLine("CREATING A PLAN");
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

        var plan = await planner.CreatePlanAsync(kernel, goal, requiredVariables);

        return plan.ToString();
    }

    [KernelFunction]
    [Description("Executes a plan.")]
    public async Task<string> ExecutePlan(
        Kernel kernel,
        [Description("The handlebars plan to be executed")] string plan)
    {
        Console.WriteLine("EXECUTING THE PLAN");
        var handlebarsPlan = new HandlebarsPlan(plan);
        return await handlebarsPlan.InvokeAsync(kernel);
    }
#pragma warning restore SKEXP0060
}