using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SemanticKernelExploration.Helper;
using SemanticKernelExploration.Plugins;

#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0010

// Create the configuration
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.UserName}.json", optional: true, reloadOnChange: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)          // ** User Secrets can be changed by right click on Project > Manage User Secrets
    .AddCommandLine(args)                                                                           // ** Makes it possible to set configuration key value pairs via "--" e.g --key=value
    .AddEnvironmentVariables()
    .Build();

// Define Agents
const string InternalLeaderName = "InternalLeader";
const string InternalLeaderInstructions =
    """
        Your job is to clearly and directly communicate the current assistant response to the user.

        If information has been requested, only repeat the request.

        If information is provided, only repeat the information.

        Do not come up with your own suggestions.
        """;

const string InternalPlanCreatorName = "InternalPlanner";
const string InternalPlanCreatorInstructions =
    """        
        You are a personal planner that provides plans for achieving goals.

        Always immediately incorporate review feedback and provide an updated response.
        """;

const string InternalPlanReviewerName = "InternalPlanReviewer";
const string InternalPlanReviewerInstructions =
    """
        Review the most recent plan response.

        It's important that you check if the plan is executable with the given information.
        
        If the plan is currently not executable requests additional information!

        Either provide critical feedback to improve the response without introducing new ideas or state that the response is adequate.
        """;

const string InternalPlanExecutorName = "InternalPlanExecutor";
const string InternalPlanExecutorInstructions =
    """
        You take the previously created plan and execute it.

        Then provide the result.
        """;

const string InnerSelectionInstructions =
    $$$"""
        Select which participant will take the next turn based on the conversation history.
        
        Only choose from these participants:
        - {{{InternalPlanCreatorName}}}
        - {{{InternalPlanReviewerName}}}
        - {{{InternalLeaderName}}}
        
        Choose the next participant according to the action of the most recent participant:
        - After user input, it is {{{InternalPlanCreatorName}}}'a turn.
        - After {{{InternalPlanCreatorName}}} replies with a plan, it is {{{InternalPlanReviewerName}}}'s turn.
        - After {{{InternalPlanReviewerName}}} requests additional information, it is {{{InternalLeaderName}}}'s turn.
        - After {{{InternalPlanReviewerName}}} provides feedback or instruction, it is {{{InternalPlanCreatorName}}}'s turn.
        - After {{{InternalPlanReviewerName}}} states the {{{InternalPlanCreatorName}}}'s response is adequate, it is {{{InternalPlanExecutorName}}}'s turn.
        - After {{{InternalPlanExecutorName}}} replies, it is {{{InternalLeaderName}}}'s turn.
        
        Respond in JSON format.  The JSON schema can include only:
        {
            "name": "string (the name of the assistant selected for the next turn)",
            "reason": "string (the reason for the participant was selected)"
        }
        
        History:
        {{${{{KernelFunctionSelectionStrategy.DefaultHistoryVariableName}}}}}
        """;

const string OuterTerminationInstructions =
    $$$"""
        Determine if user request has been fully answered.
        
        Respond in JSON format.  The JSON schema can include only:
        {
            "isAnswered": "bool (true if the user request has been fully answered)",
            "reason": "string (the reason for your determination)"
        }
        
        History:
        {{${{{KernelFunctionTerminationStrategy.DefaultHistoryVariableName}}}}}
        """;


OpenAIPromptExecutionSettings jsonSettings = new() { ResponseFormat = ChatCompletionsResponseFormat.JsonObject };

// Create the Agents
ChatCompletionAgent internalLeaderAgent = CreateAgent(InternalLeaderName, InternalLeaderInstructions);

ChatCompletionAgent internalPlanCreatorAgent = CreateAgent(InternalPlanCreatorName, InternalPlanCreatorInstructions);
internalPlanCreatorAgent.Kernel.Plugins.AddFromType<HandlebarsPlannerPlugin>();
internalPlanCreatorAgent.Kernel.Plugins.AddFromType<EmailPlugin>();

ChatCompletionAgent internalPlanReviewerAgent = CreateAgent(InternalPlanReviewerName, InternalPlanReviewerInstructions);
internalPlanReviewerAgent.Kernel.Plugins.AddFromType<HandlebarsPlannerPlugin>();
internalPlanReviewerAgent.Kernel.Plugins.AddFromType<EmailPlugin>();

ChatCompletionAgent internalPlanExecutorAgent = CreateAgent(InternalPlanExecutorName, InternalPlanExecutorInstructions);
internalPlanExecutorAgent.Kernel.Plugins.AddFromType<HandlebarsPlannerPlugin>();
internalPlanExecutorAgent.Kernel.Plugins.AddFromType<EmailPlugin>();

KernelFunction innerSelectionFunction = KernelFunctionFactory.CreateFromPrompt(InnerSelectionInstructions, jsonSettings);
KernelFunction outerTerminationFunction = KernelFunctionFactory.CreateFromPrompt(OuterTerminationInstructions, jsonSettings);

AggregatorAgent mainAgent =
    new(CreateChat)
    {
        Name = "Main",
        Mode = AggregatorMode.Nested,
    };

AgentGroupChat chat =
    new()
    {
        ExecutionSettings =
            new()
            {
                TerminationStrategy =
                    new KernelFunctionTerminationStrategy(outerTerminationFunction, CreateKernelWithChatCompletion())
                    {
                        ResultParser =
                            (result) =>
                            {
                                OuterTerminationResult? jsonResult = JsonResultTranslator.Translate<OuterTerminationResult>(result.GetValue<string>());

                                return jsonResult?.isAnswered ?? false;
                            },
                        MaximumIterations = 5,
                    },
            }
    };

AgentGroupChat CreateChat() =>
    new(internalLeaderAgent, internalPlanCreatorAgent, internalPlanReviewerAgent, internalPlanExecutorAgent)
    {
        ExecutionSettings =
            new()
            {
                SelectionStrategy =
                    new KernelFunctionSelectionStrategy(innerSelectionFunction, CreateKernelWithChatCompletion())
                    {
                        ResultParser =
                            (result) =>
                            {
                                AgentSelectionResult? jsonResult = JsonResultTranslator.Translate<AgentSelectionResult>(result.GetValue<string>());

                                string? agentName = string.IsNullOrWhiteSpace(jsonResult?.name) ? null : jsonResult?.name;
                                agentName ??= InternalPlanCreatorName;

                                Console.WriteLine($"\t>>>> INNER TURN: {agentName}");

                                return agentName;
                            }
                    },
                TerminationStrategy =
                    new AgentTerminationStrategy()
                    {
                        Agents = new List<Agent>(){ internalLeaderAgent },
                        MaximumIterations = 7,
                        AutomaticReset = true,
                    },
            }
    };

// Invoke chat and display messages.
Console.WriteLine("\n######################################");
Console.WriteLine("# DYNAMIC CHAT");
Console.WriteLine("######################################");

await InvokeChatAsync("Can you provide three original birthday gift ideas.  I don't want a gift that someone else will also pick.");

async Task InvokeChatAsync(string input)
{
    chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, input));

    Console.WriteLine($"# {AuthorRole.User}: '{input}'");

    await foreach (var content in chat.InvokeAsync(mainAgent))
    {
        Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
    }

    Console.WriteLine($"\n# IS COMPLETE: {chat.IsComplete}");
}

// ########################################################################################################################
// ### Helper functions 
// ########################################################################################################################
ChatCompletionAgent CreateAgent(string agentName, string agentInstructions) =>
    new()
    {
        Instructions = agentInstructions,
        Name = agentName,
        Kernel = CreateKernelWithChatCompletion(),
    };

Kernel CreateKernelWithChatCompletion()
{
    // Create the kernel
    var kernelBuilder = Kernel.CreateBuilder();

    // Add the configuration as singleton service for Dependency Injection (DI)
    kernelBuilder.Services.AddSingleton<IConfiguration>(config);

    kernelBuilder.Services.AddOpenAIChatCompletion(
        config["OPEN_AI_MODEL_ID"]!,
        config["OPEN_AI_API_KEY"]!
    );

    // Build the kernel
    var kernel = kernelBuilder.Build();

    return kernel;
}

record OuterTerminationResult(bool isAnswered, string reason);

record AgentSelectionResult(string name, string reason);

class AgentTerminationStrategy : TerminationStrategy
{
    /// <inheritdoc/>
    protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}


#pragma warning restore SKEXP0110
#pragma warning restore SKEXP0010