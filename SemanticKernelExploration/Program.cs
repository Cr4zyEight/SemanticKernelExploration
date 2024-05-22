using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SemanticKernelExploration.Helper;
using SemanticKernelExploration.Plugins;

#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001

// Create the configuration
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.UserName}.json", optional: true, reloadOnChange: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)          // ** User Secrets can be changed by right click on Project > Manage User Secrets
    .AddCommandLine(args)                                                                           // ** Makes it possible to set configuration key value pairs via "--" e.g --key=value
    .AddEnvironmentVariables()
    .Build();

const string PlannerName = "Planner";
const string PlannerInstructions =
    """
        You are responsible for creating, checking and executing a plan that will fulfill the users request.
        Only create ONE plan per main request.
        DO NOT create a new plan if the user gives you additional info that you asked for.
        DO NOT call any functions on your own.
        Plan execution ONLY!
        
        You MUST follow the following steps in the given order!

        ## Steps
        1. Create the plan invoking 'CreatePlan' to fulfill the users request. Then print out the plan.
        2. Analyze the plan and check if all required parameters are given to execute the plan. Ask the user for each missing parameter, if necessary.
        3. When all parameters have been provided, invoke 'ExecutePlan' and respond with the result. If the plan is not created successfully, start again at step 1.
        """;

ChatCompletionAgent plannerAgent =
    new()
    {
        Instructions = PlannerInstructions,
        Name = PlannerName,
        Kernel = CreateKernelWithChatCompletion(config),
        ExecutionSettings = new OpenAIPromptExecutionSettings() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, MaxTokens = 1024 },
    };
// Start the conversation
string? input = null;
var chathistory = new ChatHistory();
while (true)
{
    Console.Write("User > ");
    input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        break;
    }
    chathistory.AddUserMessage(input);
    // Invoke chat and display messages.
    await foreach (var content in plannerAgent.InvokeAsync(chathistory))
    {
        Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
        chathistory.Add(content);
    }
}

Kernel CreateKernelWithChatCompletion(IConfigurationRoot configurationRoot)
{
    // Create the kernel
    var kernelBuilder = Kernel.CreateBuilder();

    // Add the configuration as singleton service for Dependency Injection (DI)
    kernelBuilder.Services.AddSingleton<IConfiguration>(configurationRoot);

    // Add Plugins to Semantic Kernel
    kernelBuilder.Plugins.AddFromType<EmailPlugin>();
    kernelBuilder.Plugins.AddFromType<HandlebarsPlannerPlugin>();

    kernelBuilder.Services.AddOpenAIChatCompletion(
        configurationRoot["OPEN_AI_MODEL_ID"]!,
        configurationRoot["OPEN_AI_API_KEY"]!
    );

    // Build the kernel
    var kernel = kernelBuilder.Build();
    return kernel;
}

#pragma warning restore SKEXP0001
#pragma warning restore SKEXP0110
#pragma warning restore SKEXP0010