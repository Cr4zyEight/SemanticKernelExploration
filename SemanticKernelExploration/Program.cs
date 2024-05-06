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
        If you are requesteted to CREATE a plan call the 'CreatePlan' function in the 'HandlebarsPlannerPlugin' and respond the result.
        If you are requested to EXECUTE a plan call the 'ExecutePlan' function in the 'HandlebarsPlannerPlugin' and respond the result.
        Always immediately incorporate review feedback and provide an updated response.
        """;

const string PlanReviewerName = "PlanReviewer";
const string PlanReviewerInstructions =
    """
        You check handlebars plans to see if all the necessary variables are given. 
        Request any missing information before giving the feedback.
        """;

// Define the agents
ChatCompletionAgent plannerAgent =
    new()
    {
        Instructions = PlannerInstructions,
        Name = PlannerName,
        Kernel = CreateKernelWithChatCompletion(config),
        ExecutionSettings = new OpenAIPromptExecutionSettings() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions },
    };

ChatCompletionAgent planReviewerAgent =
    new()
    {
        Instructions = PlanReviewerInstructions,
        Name = PlanReviewerName,
        Kernel = CreateKernelWithChatCompletion(config)
    };

KernelFunction terminationFunction =
    KernelFunctionFactory.CreateFromPrompt(
        $$$"""
                Determine if user request has been fully answered.

                History:
                {{${{{KernelFunctionSelectionStrategy.DefaultHistoryVariableName}}}}}
                """);

KernelFunction selectionFunction =
    KernelFunctionFactory.CreateFromPrompt(
        $$$"""
                Your job is to determine which participant takes the next turn in a conversation according to the action of the most recent participant.
                State only the name of the participant to take the next turn.

                Choose only from these participants:
                - {{{PlannerName}}}
                - {{{PlanReviewerName}}}

                Always follow these rules when selecting the next participant:
                - After user input, it is {{{PlannerName}}}'s turn.
                - After {{{PlannerName}}} replies, it is {{{PlanReviewerName}}}'s turn.
                - Interpret {{{PlanReviewerName}}}'s response and decide the next step
                
                History:
                {{${{{KernelFunctionSelectionStrategy.DefaultHistoryVariableName}}}}}
                """);

// Create a chat for agent interaction.
AgentGroupChat chat =
    new(plannerAgent, planReviewerAgent)
    {
        ExecutionSettings =
            new()
            {
                // Here KernelFunctionTerminationStrategy will terminate
                // when the plan reviewer has given their approval.
                TerminationStrategy =
                    new KernelFunctionTerminationStrategy(terminationFunction, CreateKernelWithChatCompletion(config))
                    {
                        // Only the plan reviewer may approve.
                        Agents = new List<Agent>() { planReviewerAgent },
                        // Customer result parser to determine if the response is "exit"
                        ResultParser = (result) => result.GetValue<string>()?.Contains("exit", StringComparison.OrdinalIgnoreCase) ?? false,
                        // Limit total number of turns
                        MaximumIterations = 10,
                    },
                // Here a KernelFunctionSelectionStrategy selects agents based on a prompt function.
                SelectionStrategy =
                    new KernelFunctionSelectionStrategy(selectionFunction, CreateKernelWithChatCompletion(config))
                    {
                        // Returns the entire result value as a string.
                        ResultParser = (result) => result.GetValue<string>() ?? PlannerName,
                    },
            }
    };

// Start the conversation
string? input = null;

do
{
    Console.Write("User > ");
    input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        // Leaves if the user hit enter without typing any word
        break;
    }

    chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, input));
    // Invoke chat and display messages.

    await foreach (var content in chat.InvokeAsync())
    {
        Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
    }

    Console.WriteLine($"# IS COMPLETE: {chat.IsComplete}");
} while (!chat.IsComplete);


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