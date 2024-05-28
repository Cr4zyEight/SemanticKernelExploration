using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using SemanticKernelExploration.Models;

namespace SemanticKernelExploration.Plugins;

public class EmailPlugin
{
    private const string CacheFolderPath = "Cache";
    private const string CacheFileName = "emailCache.json";
    public static string CacheFilePath => Path.Combine(CacheFolderPath, CacheFileName);

    //private readonly IConfiguration _configuration;

    public EmailPlugin()
    {
        //_configuration = configuration;
    }

    [KernelFunction]
    [Description("Gets all E-Mails. Check if all parameters are given by the user")]
    public static List<EmailMessage> GetEmails(
        [Description("The IMAP Host adress.")] string imapHost,
        [Description("The IMAP Port.")] int imapPort,
        [Description("The IMAP User Name.")] string imapUserName,
        [Description("The IMAP Password.")] string imapPassword)
    {
        // Fetch new emails
        var messages = FetchEmailsFromServer(imapHost, imapPort, imapUserName, imapPassword);

        // Save updated list to cache
        var updatedJson = JsonSerializer.Serialize(messages);
        File.WriteAllText(CacheFilePath, updatedJson);

        return messages;
    }

    [KernelFunction]
    [Description("Counts the E-Mails")]
    public static int CountEmails(
        [Description("The collection of E-Mails that should be counted")] List<EmailMessage> emails
        )
    {
        return emails.Count;
    }

    [KernelFunction]
    [Description("Sort a collection of E-Mails by date ascending")]
    public List<EmailMessage> SortEmailsAscending(
        [Description("The collection of E-Mails that should be sorted")] List<EmailMessage> emails
    )
    {
        var sortedEmails = emails.OrderBy(email =>
        {
            DateTime date;
            DateTime.TryParseExact(email.Date, "dd.MM.yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            return date;
        }).ToList();

        return sortedEmails;
    }

    [KernelFunction]
    [Description("Sort a collection of E-Mails by date descending")]
    public List<EmailMessage> SortEmailsDescending(
        [Description("The collection of E-Mails that should be sorted")] List<EmailMessage> emails
    )
    {
        var sortedEmails = emails.OrderByDescending(email =>
        {
            DateTime.TryParseExact(email.Date, "dd.MM.yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            return date;
        }).ToList();

        return sortedEmails;
    }

    [KernelFunction]
    [Description("Get an amount of E-Mails from a collection")]
    public List<EmailMessage> GetFirstEmails(
        [Description("The collection of E-Mails")] List<EmailMessage> emails,
        [Description("The amount of E-Mails that should be fetched from the list")] int amount
    )
    {
        return emails.Take(amount).ToList();
    }

    private static List<EmailMessage> FetchEmailsFromServer(string imapHost, int imapPort, string imapUserName, string imapPassword)
    {
        // Initialize the IMAP client
        var client = new ImapClient();

        // Connect to the server and authenticate
        client.Connect(imapHost, imapPort, true); // Use SSL
        client.Authenticate(imapUserName, imapPassword);

        // Select the mailbox you want to work with
        client.Inbox.Open(FolderAccess.ReadOnly);
        var items = client.Inbox.Fetch(0, 100, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId).ToList();

        var messages = new List<EmailMessage>();

        // Ensure the Cache folder exists
        Directory.CreateDirectory(CacheFolderPath);

        // Read from cache
        if (File.Exists(CacheFilePath))
        {
            var json = File.ReadAllText(CacheFilePath);
            messages = JsonSerializer.Deserialize<List<EmailMessage>>(json);
        }

        var cachedMailIds = messages.Select(m => m.Id).ToList();

        items.RemoveAll(i => cachedMailIds.Contains(i.Envelope.MessageId));

        foreach (var item in items)
        {
            var message = client.Inbox.GetMessage(item.UniqueId);
            messages.Add(new EmailMessage
            {
                Id = message.MessageId,
                From = string.Join(", ", message.From),
                To = string.Join(", ", message.To),
                Date = message.Date.ToString("dd.MM.yyyy - HH:mm:ss"),
                Subject = message.Subject,
                Body = message.TextBody,
                // Add other properties as needed
            });
        }

        client.Disconnect(true);

        return messages;
    }
}