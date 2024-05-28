using System.Reflection;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.Postgres;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Npgsql;
using SemanticKernelExploration.Plugins;

#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0050

// Create the configuration
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.UserName}.json", optional: true, reloadOnChange: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true)          // ** User Secrets can be changed by right click on Project > Manage User Secrets
    .AddCommandLine(args)                                                                           // ** Makes it possible to set configuration key value pairs via "--" e.g --key=value
    .AddEnvironmentVariables()
    .Build();

var memory = new KernelMemoryBuilder()
    .WithOpenAIDefaults(config["OPEN_AI_API_KEY"])
    .Build<MemoryServerless>();

await SaveEmailsToMemory(memory);
var answer = await memory.AskAsync("Tell me something about Chester Bennington?");
//var answer = await memory.AskAsync("Give me the subjects of my 3 newest mails?");

Console.WriteLine(answer.Result + "/n");

var kernel = CreateKernelWithChatCompletion(config);

NpgsqlDataSourceBuilder dataSourceBuilder = new NpgsqlDataSourceBuilder(config["KernelMemory:Services:Postgres:ConnectionString"]);
dataSourceBuilder.UseVector();
NpgsqlDataSource dataSource = dataSourceBuilder.Build();

var memoryWithPostgres = new MemoryBuilder()
    .WithPostgresMemoryStore(dataSource, vectorSize: 1536, schema: "public")
    .WithOpenAITextEmbeddingGeneration("text-embedding-ada-002", config["OPEN_AI_API_KEY"]!)
    .Build();

kernel.ImportPluginFromObject(new TextMemoryPlugin(memoryWithPostgres), "memory");

var chat = kernel.GetRequiredService<IChatCompletionService>();

OpenAIPromptExecutionSettings settings = new() { ToolCallBehavior = ToolCallBehavior.EnableKernelFunctions };

//await StoreMemoryAsync(memoryWithPostgres);

//await SearchMemoryAsync(memoryWithPostgres, "Give me the newest mails");


// Start the conversation
string? input = null;
var chatHistory = new ChatHistory();
while (true)
{
    Console.Write("User > ");
    input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        break;
    }

    chatHistory.AddUserMessage(input);

    ChatMessageContent result = await chat.GetChatMessageContentAsync(chatHistory, settings, kernel);

    if (result.Content is not null)
    {
        Console.Write(result.Content);
    }

    IEnumerable<FunctionCallContent> functionCalls = FunctionCallContent.GetFunctionCalls(result);
    if (!functionCalls.Any())
    {
        break;
    }

    chatHistory.Add(result); // Adding LLM response containing function calls(requests) to chat history as it's required by LLMs.

    foreach (var functionCall in functionCalls)
    {
        try
        {
            FunctionResultContent resultContent = await functionCall.InvokeAsync(kernel); // Executing each function.

            chatHistory.Add(resultContent.ToChatMessage());
        }
        catch (Exception ex)
        {
            chatHistory.Add(new FunctionResultContent(functionCall, ex).ToChatMessage()); // Adding function result to chat history.
            // Adding exception to chat history.
            // or
            //string message = "Error details that LLM can reason about.";
            //chatHistory.Add(new FunctionResultContent(functionCall, message).ToChatMessageContent()); // Adding function result to chat history.
        }
    }

    Console.WriteLine();
}

Kernel CreateKernelWithChatCompletion(IConfigurationRoot configurationRoot)
{
    // Create the kernel
    var kernelBuilder = Kernel.CreateBuilder();

    // Add the configuration as singleton service for Dependency Injection (DI)
    kernelBuilder.Services.AddSingleton<IConfiguration>(configurationRoot);

    // Add Plugins to Semantic Kernel
    //kernelBuilder.Plugins.AddFromType<EmailPlugin>();
    //kernelBuilder.Plugins.AddFromType<HandlebarsPlannerPlugin>();

    kernelBuilder.Services.AddOpenAIChatCompletion(
        configurationRoot["OPEN_AI_MODEL_ID"]!,
        configurationRoot["OPEN_AI_API_KEY"]!
    );

    // Build the kernel
    var kernel = kernelBuilder.Build();
    return kernel;
}

async Task SaveEmailsToMemory(IKernelMemory memory)
{
    //var emails = EmailPlugin.GetEmails("mail.wkg-software.de", 993, "frebel@wkg-software.de", "Sp4rt4n117!H4l0");
    //foreach (var email in emails)
    //{
    //    await memory.ImportTextAsync(
    //        text: email.Body,
    //        tags: new()
    //        {
    //            { nameof(email.Id), email.Id },
    //            { nameof(email.Date), email.Date },
    //            { nameof(email.From), email.From },
    //            { nameof(email.To), email.To },
    //            { nameof(email.Subject), email.Subject }
    //        });
    //}
    await memory.ImportWebPageAsync("https://en.wikipedia.org/wiki/Linkin_Park", "Linkin Park");
}

async Task StoreMemoryAsync(ISemanticTextMemory memory)
{
    var emails = EmailPlugin.GetEmails("mail.wkg-software.de", 993, "frebel@wkg-software.de", "Sp4rt4n117!H4l0");
    var i = 0;
    foreach (var email in emails)
    {
        await memory.SaveInformationAsync(
            id: email.Id,
            collection: "emails_test_collection_info",
            text: $@"Date: {email.Date}
Sender: {email.From}
Receiver: {email.To}
Subject: {email.Subject}
Body: {email.Body}");

//        await memory.SaveReferenceAsync(
//            collection: "emails_test_collection",
//            externalSourceName: "emails_test_externalsourcename",
//            externalId: email.Id,
//            description: string.Empty,
//            text: @$"Date: {email.Date}
//Sender: {email.From}
//Receiver: {email.To}
//Subject: {email.Subject}
//Body: {email.Body}");
    }

    Console.WriteLine($" #{++i} saved.");
}

async Task SearchMemoryAsync(ISemanticTextMemory memory, string query)
{
    Console.WriteLine("\nQuery: " + query + "\n");

    var memoryResults = memory.SearchAsync("emails_test_collection_info", query, limit: 10, minRelevanceScore: 0.75);

    int i = 0;
    await foreach (MemoryQueryResult memoryResult in memoryResults)
    {
        Console.WriteLine($"Result {++i}:");
        Console.WriteLine("  URL:     : " + memoryResult.Metadata.Id);
        Console.WriteLine("  Relevance: " + memoryResult.Relevance);
        Console.WriteLine("  Text    : " + memoryResult.Metadata.Text);
        Console.WriteLine();
    }

    Console.WriteLine("----------------------");
}

#pragma warning restore SKEXP0020
#pragma warning restore SKEXP0001
#pragma warning restore SKEXP0110
#pragma warning restore SKEXP0010
#pragma warning restore SKEXP0050
