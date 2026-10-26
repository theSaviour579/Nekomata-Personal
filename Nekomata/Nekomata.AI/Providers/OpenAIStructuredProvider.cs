using Microsoft.Extensions.Configuration;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Models.Actions;
using Nekomata.AI.Models.Email;
using Nekomata.AI.Models.Meetings;
using Nekomata.AI.Privacy;
using Nekomata.AI.Schemas;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
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
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

    public OpenAIStructuredProvider(
        IConfiguration configuration)
    {
        var apiKey =
            (configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key not found or is empty.");
        }

        var clientOptions = new OpenAIClientOptions();
        clientOptions.AddPolicy(new RequestShapePolicy(), PipelinePosition.PerCall);

        _chatClient = new ChatClient(
            model: "gpt-5.1",
            credential: new ApiKeyCredential(apiKey),
            options: clientOptions);
    }

    /// <summary>
    /// Structure-only description of the last request this provider sent (property names,
    /// model value, size). Never contains prompt text. Used to explain provider failures.
    /// </summary>
    public static string LastRequestShape { get; private set; } = "no request sent yet";

    private sealed class RequestShapePolicy : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Capture(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Capture(message);
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        }

        private static void Capture(PipelineMessage message)
        {
            try
            {
                var request = message.Request;
                var uri = request.Uri;
                var content = request.Content;
                if (content is null)
                {
                    LastRequestShape = $"{request.Method} {uri?.Host}{uri?.AbsolutePath}; no body";
                    return;
                }

                using var buffer = new MemoryStream();
                content.WriteTo(buffer, CancellationToken.None);

                buffer.Position = 0;

                string properties;
                string model = "(none)";
                try
                {
                    using var document = JsonDocument.Parse(buffer);
                    var names = new List<string>();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        names.Add(property.Name);
                        if (property.NameEquals("model") && property.Value.ValueKind == JsonValueKind.String)
                            model = property.Value.GetString() ?? "(null)";
                    }
                    properties = string.Join(",", names);
                }
                catch (JsonException)
                {
                    properties = "body is not valid JSON";
                }

                request.Headers.TryGetValue("Content-Type", out var contentType);
                LastRequestShape = $"{request.Method} {uri?.Host}{uri?.AbsolutePath}; {buffer.Length} bytes; content-type={contentType}; properties={properties}; model={model}";
            }
            catch (Exception ex)
            {
                LastRequestShape = "could not inspect request (" + ex.GetType().Name + ")";
            }
        }
    }

    public Task<T?> AskStructuredAsync<T>(
        string systemPrompt,
        string userPrompt)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        // Names, addresses, phone numbers, customers and amounts are swapped for placeholders before sending,
        // and put back in the answer here (AI-use policy).
        var guard = new Pseudonymiser();
        var user = guard.Mask(userPrompt);
        var system = guard.Mask(systemPrompt);
        if (guard.Masked) system += "\n\n" + Pseudonymiser.Instruction;
        AiSendLog.Record(new(DateTimeOffset.Now, "structured:" + typeof(T).Name, "gpt-5.1", system.Length + user.Length, guard.Counts));
        return CompleteStructuredAsync<T>([new SystemChatMessage(system), new UserChatMessage(user)], guard);
    }

    // Pictures can't have names taken out, so screenshots are never sent (AI-use policy). "Screen this" reads the
    // window's text on the PC instead and sends that, with placeholders.
    public Task<T?> AskStructuredWithImageAsync<T>(string systemPrompt, string userPrompt, byte[] png) where T : class
    {
        AiSendLog.Record(new(DateTimeOffset.Now, "image:" + typeof(T).Name, "gpt-5.1", 0, new Dictionary<string, int>(), Refused: true,
            Note: "screenshots are not sent"));
        throw new NotSupportedException("Screenshots aren't sent to the AI under the AI-use policy.");
    }

    private async Task<T?> CompleteStructuredAsync<T>(List<ChatMessage> messages, Pseudonymiser guard) where T : class
    {
        var schema = GetSchema<T>();

        var options = new ChatCompletionOptions
        {
            ResponseFormat =
                ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: schema.Name,
                    jsonSchema: BinaryData.FromString(schema.Json),
                    jsonSchemaIsStrict: true)
        };

        ChatCompletion completion;
        try
        {
            completion =
                await _chatClient.CompleteChatAsync(
                    messages,
                    options);
        }
        catch (ClientResultException ex)
        {
            throw new InvalidOperationException(
                $"{ex.Message} [request: {LastRequestShape}]",
                ex);
        }

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
        json = guard.Unmask(json, json: true);

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
        if (typeof(T) == typeof(MeetingFollowThrough)) return ("meeting_follow_through", MeetingFollowThroughSchema.Json);
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

        if (typeof(T) == typeof(ChaseConversationAdvice))
            return ("chase_conversation_advice", ChaseConversationAdviceSchema.Json);
        if (typeof(T) == typeof(TranscriptFollowThrough))
            return ("transcript_follow_through", TranscriptFollowThroughSchema.Json);
        if (typeof(T) == typeof(ReplyTriage))
            return ("reply_triage", ReplyTriageSchema.Json);
        if (typeof(T) == typeof(EmailPromiseExtraction))
            return ("email_promise_extraction", EmailPromiseExtractionSchema.Json);
        if (typeof(T) == typeof(MeetingSpokenBrief))
            return ("meeting_spoken_brief", MeetingSpokenBriefSchema.Json);

        // Result classes without a hand-written schema (Teams review and similar).
        return ReflectionSchema.For(typeof(T));
    }
}
