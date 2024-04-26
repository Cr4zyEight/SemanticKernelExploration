using System.Reflection;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;
using SemanticKernelExploration.Plugins;
using SemanticKernelExploration.Resources;
using Microsoft.SemanticKernel.ChatCompletion;

// Create the kernel
var kernelBuilder = Kernel.CreateBuilder();

// Create the configuration
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.UserName}.json", optional: true, reloadOnChange: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)          // ** User Secrets can be changed by right click on Project > Manage User Secrets
    .AddCommandLine(args)                                                                           // ** Makes it possible to set configuration key value pairs via "--" e.g --key=value
    .AddEnvironmentVariables()
    .Build();

// Add the configuration as singleton service for Dependency Injection (DI)
kernelBuilder.Services.AddSingleton<IConfiguration>(config);

// Add EmailPlugin to Semantic Kernel
kernelBuilder.Plugins.AddFromType<EmailPlugin>();

kernelBuilder.Services.AddOpenAIChatCompletion(
    config["OPEN_AI_MODEL_ID"]!,
    config["OPEN_AI_API_KEY"]!
);

// Build the kernel
var kernel = kernelBuilder.Build();

// Retrieve the chat completion service from the kernel
IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Create the chat history
var history = new ChatHistory(@"You are a friendly assistant who likes to follow the rules. You will complete required steps
and request approval before taking any consequential actions. If the user doesn't provide
enough information for you to complete a task, you will keep asking questions until you have
enough information to complete the task.");

var executionSettings = new OpenAIPromptExecutionSettings()
{
    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    Temperature = 0.0,
    TopP = 0.1
};

#pragma warning disable SKEXP0060 // HandlebarsPlanner Nuget Package is still 1.8.0-preview (https://www.nuget.org/packages/Microsoft.SemanticKernel.Planners.Handlebars)

var planner = new HandlebarsPlanner(new HandlebarsPlannerOptions()
{
    AllowLoops = true,
    ExecutionSettings = executionSettings

    // Callback to override the default prompt template.
    // CreatePlanPromptHandler = OverridePlanPrompt
});

//Start the conversation
while (true)
{
    var userPrompt = string.Empty;
    var fullResultMessage = string.Empty;
    HandlebarsPlan? plan = null;

    while (!fullResultMessage.Contains("G0"))
    {
        fullResultMessage = string.Empty;

        Console.Write("User > ");

        userPrompt += Console.ReadLine()!;

        plan = await planner.CreatePlanAsync(kernel, userPrompt);

        history.AddUserMessage($@"The user has the following request: 
{userPrompt}

The following plan was created to achieve the goal of the request:
{plan}

Check if the user request contains all necessary information to fill all the required variables that are needed for the plan, otherwise ask for it.
If the user request already contains all information just type 'G0'.
");

        var result = chatCompletionService.GetStreamingChatMessageContentsAsync(
            history,
            executionSettings: executionSettings,
            kernel: kernel);

        await foreach (var content in result)
        {
            if (content.Role.HasValue)
            {
                Console.Write("MY GPT > ");
            }
            Console.Write(content.Content);
            fullResultMessage += content.Content;
        }
        
        Console.WriteLine();

        // Add the message from the agent to the chat history
        history.AddAssistantMessage(fullResultMessage);
    }

#if DEBUG
    Console.WriteLine("Plan [ONLY DEBUG OUTPUT]:");
    Console.WriteLine(plan);
    Console.WriteLine();
#endif
    var planResult = string.Empty;

    try
    {
        planResult = await plan!.InvokeAsync(kernel);
    }
    catch (Exception e)
    {
        Console.WriteLine("The plan could not be executed");
        Console.WriteLine($"Exception: {e}");
        Console.WriteLine($"Inner Exception: {e.InnerException}");
    }

    Console.WriteLine($"MY GPT > {planResult}");
    Console.WriteLine();
}
#pragma warning restore SKEXP0060


static string OverridePlanPrompt()
{
    // Load a custom CreatePlan prompt template from an embedded resource.
    var ResourceFileName = "prompt-override.handlebars";
    var fileContent = EmbeddedResource.ReadStream(ResourceFileName);
    return new StreamReader(fileContent!).ReadToEnd();
}