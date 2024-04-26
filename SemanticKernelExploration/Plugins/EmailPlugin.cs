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
    private string _cacheFilePath => Path.Combine(CacheFolderPath, CacheFileName);

    private readonly IConfiguration _configuration;

    public EmailPlugin(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [KernelFunction]
    [Description("Gets all E-Mails. Check if all parameters are given by the user")]
    public async Task<List<EmailMessage>> GetEmailsAsync(
        [Description("The IMAP Host adress.")] string imapHost,
        [Description("The IMAP Port.")] int imapPort,
        [Description("The IMAP User Name.")] string imapUserName,
        [Description("The IMAP Password.")] string imapPassword)
    {
        // Fetch new emails
        var messages = await FetchEmailsFromServerAsync(imapHost, imapPort, imapUserName, imapPassword);

        // Save updated list to cache
        var updatedJson = JsonSerializer.Serialize(messages);
        await File.WriteAllTextAsync(_cacheFilePath, updatedJson);

        return messages;
    }

    [KernelFunction]
    [Description("Counts the E-Mails")]
    public Task<int> CountEmailsAsync(
        [Description("The collection of E-Mails that should be counted")] List<EmailMessage> emails
        )
    {
        return Task.FromResult(emails.Count);
    }

    [KernelFunction]
    [Description("Sort a collection of E-Mails by date ascending")]
    public async Task<List<EmailMessage>> SortEmailsAscendingAsync(
        [Description("The collection of E-Mails that should be sorted")] List<EmailMessage> emails
    )
    {
        return await Task.Run(() =>
        {
            var sortedEmails = emails.OrderBy(email =>
            {
                DateTime date;
                DateTime.TryParseExact(email.Date, "dd.MM.yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                return date;
            }).ToList();

            return sortedEmails;
        });
    }

    [KernelFunction]
    [Description("Sort a collection of E-Mails by date descending")]
    public async Task<List<EmailMessage>> SortEmailsDescendingAsync(
        [Description("The collection of E-Mails that should be sorted")] List<EmailMessage> emails
    )
    {
        return await Task.Run(() =>
        {
            var sortedEmails = emails.OrderByDescending(email =>
            {
                DateTime.TryParseExact(email.Date, "dd.MM.yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
                return date;
            }).ToList();

            return sortedEmails;
        });
    }

    [KernelFunction]
    [Description("Get an amount of E-Mails from a collection")]
    public Task<List<EmailMessage>> GetFirstEmails(
        [Description("The collection of E-Mails")] List<EmailMessage> emails,
        [Description("The amount of E-Mails that should be fetched from the list")] int amount
    )
    {
        return Task.FromResult(emails.Take(amount).ToList());
    }

    private async Task<List<EmailMessage>> FetchEmailsFromServerAsync(string imapHost, int imapPort, string imapUserName, string imapPassword)
    {
        // Initialize the IMAP client
        var client = new ImapClient();

        // Connect to the server and authenticate
        await client.ConnectAsync(imapHost, imapPort, true); // Use SSL
        await client.AuthenticateAsync(imapUserName, imapPassword);

        // Select the mailbox you want to work with
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
        var items = (await client.Inbox.FetchAsync(0, 100, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId)).ToList();

        var messages = new List<EmailMessage>();

        // Ensure the Cache folder exists
        Directory.CreateDirectory(CacheFolderPath);

        // Read from cache
        if (File.Exists(_cacheFilePath))
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath);
            messages = JsonSerializer.Deserialize<List<EmailMessage>>(json);
        }

        var cachedMailIds = messages.Select(m => m.Id).ToList();

        items.RemoveAll(i => cachedMailIds.Contains(i.Envelope.MessageId));

        foreach (var item in items)
        {
            var message = await client.Inbox.GetMessageAsync(item.UniqueId);
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

        await client.DisconnectAsync(true);

        return messages;
    }
}