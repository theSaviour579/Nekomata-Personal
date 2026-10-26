using Microsoft.Extensions.Configuration;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Privacy;
using OpenAI.Chat;
using System.Text.Json;

namespace Nekomata.AI.Providers;

public class OpenAIProvider : IAIProvider
{
    private readonly ChatClient _chatClient;

    public OpenAIProvider(IConfiguration configuration)
    {
        var apiKey =
            (configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key not found or is empty.");

        _chatClient = new ChatClient(
            model: "gpt-5.1",
            apiKey: apiKey);
    }

    public async Task<string> AskAsync(string prompt)
    {
        try
        {
            // Names, addresses, phone numbers, customers and amounts are swapped for placeholders before sending,
            // and put back in the answer here (AI-use policy).
            var guard = new Pseudonymiser();
            var maskedPrompt = guard.Mask(prompt);
            var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are Guardian, the AI assistant inside Nekomata. Be concise, practical, and helpful." +
                (guard.Masked ? " " + Pseudonymiser.Instruction : "")),

            new UserChatMessage(maskedPrompt)
        };
            AiSendLog.Record(new(DateTimeOffset.Now, "text", "gpt-5.1", maskedPrompt.Length, guard.Counts));
            System.Diagnostics.Debug.WriteLine(
    $"Guardian prompt length: {prompt.Length:N0} characters");
            ChatCompletion completion =
                await _chatClient.CompleteChatAsync(messages);

            var text =
                completion.Content?
                    .FirstOrDefault()?
                    .Text;

            return string.IsNullOrWhiteSpace(text)
                ? "Guardian received an empty response."
                : guard.Unmask(text);
        }
        catch (Exception ex)
        {
            return
                $"OPENAI ERROR:{Environment.NewLine}{Environment.NewLine}{ex}";
        }
    }

    private static string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];

        else if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3];

        return cleaned.Trim();
    }

    public async Task<T?> AskJsonAsync<T>(string prompt)
    {
        var response = await AskAsync(prompt);

        var json = CleanJsonResponse(response);

        try
        {
            return JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Guardian returned invalid JSON. Response: {response}",
                ex);
        }
    }
}