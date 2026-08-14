using Microsoft.Extensions.Configuration;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Models.Actions;
using Nekomata.AI.Models.Meetings;
using Nekomata.AI.Schemas;
using OpenAI.Chat;
using System.Text.Json;

namespace Nekomata.AI.Providers;

public class OpenAIStructuredProvider
    : IStructuredAIProvider
{
    private readonly ChatClient _chatClient;

    private static readonly JsonSerializerOptions
        SerializerOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

    public OpenAIStructuredProvider(
        IConfiguration configuration)
    {
        var apiKey =
            configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key not found or is empty.");
        }

        _chatClient = new ChatClient(
            model: "gpt-5.1",
            apiKey: apiKey);
    }

    public async Task<T?> AskStructuredAsync<T>(
        string systemPrompt,
        string userPrompt)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var schema = GetSchema<T>();

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat =
                ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: schema.Name,
                    jsonSchema: BinaryData.FromString(schema.Json),
                    jsonSchemaIsStrict: true)
        };

        ChatCompletion completion =
            await _chatClient.CompleteChatAsync(
                messages,
                options);

        var json =
            completion.Content?
                .FirstOrDefault()?
                .Text;

        System.Diagnostics.Debug.WriteLine(
    "========== Guardian Structured JSON ==========");

        System.Diagnostics.Debug.WriteLine(json);

        System.Diagnostics.Debug.WriteLine(
            "=============================================");

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(
                json,
                SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Guardian returned invalid structured JSON for {typeof(T).Name}.{Environment.NewLine}{Environment.NewLine}{json}",
                ex);
        }
    }

    private static (string Name, string Json)
      GetSchema<T>()
    {
        if (typeof(T) == typeof(MeetingAnalysisResponse))
        {
            return (
                MeetingAnalysisSchema.Name,
                MeetingAnalysisSchema.Json);
        }

        if (typeof(T) == typeof(GuardianActionResponse))
        {
            return (
                GuardianConversationSchema.Name,
                GuardianConversationSchema.Json);
        }

        throw new NotSupportedException(
            $"No schema has been registered for {typeof(T).Name}.");
    }
}