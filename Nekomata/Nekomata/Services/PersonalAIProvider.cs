using Microsoft.Extensions.Configuration;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Providers;
using Nekomata.AI.Models.Actions;
using Nekomata.AI.Models.Meetings;
using Nekomata.AI.Schemas;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Copilot;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nekomata.UI.Services;

public sealed class PersonalAIProvider(
    PersonalSecretService secrets,
    IConfiguration configuration,
    PersonalProfileService profile,
    IMicrosoftAuthenticationService microsoftAuthentication,
    CopilotChatService copilot) : IAIProvider, IStructuredAIProvider
{
    private const string NotConfigured = "The selected AI provider is not configured. Choose Microsoft 365 Copilot, configure Microsoft AI, add an OpenAI API key, or select No AI.";
    private static readonly HttpClient MicrosoftClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private string MicrosoftEndpoint => FirstConfigured(profile.Current.AzureOpenAIEndpoint, configuration["AzureOpenAI:Endpoint"]);
    private string MicrosoftDeployment => FirstConfigured(profile.Current.AzureOpenAIDeployment, configuration["AzureOpenAI:Deployment"]);
    public bool IsMicrosoftConfigured => Uri.TryCreate(MicrosoftEndpoint, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(MicrosoftDeployment);
    public bool IsOpenAiConfigured => !string.IsNullOrWhiteSpace(secrets.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    private string Selection => string.IsNullOrWhiteSpace(profile.Current.ConversationProvider) ? "Automatic" : profile.Current.ConversationProvider;
    public bool IsConfigured => Selection switch
    {
        CopilotChatService.ProviderName => true,
        "Microsoft AI (Entra)" => IsMicrosoftConfigured,
        "OpenAI" => IsOpenAiConfigured,
        "No AI" => false,
        _ => IsMicrosoftConfigured || IsOpenAiConfigured
    };
    public string ActiveProviderName => Selection switch
    {
        CopilotChatService.ProviderName => CopilotChatService.ProviderName,
        "Microsoft AI (Entra)" => IsMicrosoftConfigured ? "Microsoft AI (Entra)" : "Microsoft AI selected · not configured",
        "OpenAI" => IsOpenAiConfigured ? "OpenAI" : "OpenAI selected · not configured",
        "No AI" => "No AI",
        _ => IsMicrosoftConfigured ? "Microsoft AI (Entra) · automatic" : IsOpenAiConfigured ? "OpenAI · automatic" : "Not configured"
    };

    public async Task<string> AskAsync(string prompt)
    {
        if (Selection == CopilotChatService.ProviderName)
            return await copilot.AskAsync(prompt, profile.Current.CopilotWebSearchEnabled);
        if (Selection == "Microsoft AI (Entra)")
            return IsMicrosoftConfigured ? await AskMicrosoftAsync("You are Guardian, the AI assistant inside Nekomata. Be concise, practical, and helpful.", prompt) : NotConfigured;
        if (Selection == "OpenAI")
            return CreateProvider() is { } selectedOpenAi ? await selectedOpenAi.AskAsync(prompt) : NotConfigured;
        if (Selection == "No AI") return NotConfigured;
        if (IsMicrosoftConfigured)
        {
            try { return await AskMicrosoftAsync("You are Guardian, the AI assistant inside Nekomata. Be concise, practical, and helpful.", prompt); }
            catch when (IsOpenAiConfigured) { }
        }
        var provider = CreateProvider();
        return provider is null ? NotConfigured : await provider.AskAsync(prompt);
    }

    public async Task<T?> AskJsonAsync<T>(string prompt)
    {
        if (Selection is CopilotChatService.ProviderName or "No AI")
            throw new NotSupportedException($"{Selection} is not enabled for structured automation. Select Microsoft AI, OpenAI or Automatic explicitly.");
        if (Selection == "Microsoft AI (Entra)")
        {
            if (!IsMicrosoftConfigured) return default;
            var selectedResponse = await AskMicrosoftAsync("Return only valid JSON for the requested result.", prompt);
            return JsonSerializer.Deserialize<T>(CleanJson(selectedResponse), JsonOptions);
        }
        if (Selection == "OpenAI") return CreateProvider() is { } selectedOpenAi ? await selectedOpenAi.AskJsonAsync<T>(prompt) : default;
        if (IsMicrosoftConfigured)
        {
            try
            {
                var response = await AskMicrosoftAsync("Return only valid JSON for the requested result.", prompt);
                return JsonSerializer.Deserialize<T>(CleanJson(response), JsonOptions);
            }
            catch when (IsOpenAiConfigured) { }
        }
        var provider = CreateProvider();
        return provider is null ? default : await provider.AskJsonAsync<T>(prompt);
    }

    public async Task<T?> AskStructuredAsync<T>(string systemPrompt, string userPrompt) where T : class
    {
        if (Selection == CopilotChatService.ProviderName)
        {
            if (typeof(T) != typeof(GuardianActionResponse))
                throw new NotSupportedException("Microsoft 365 Copilot is selected for conversation only. This feature requires Microsoft AI or OpenAI and will not fall back automatically.");
            var message = await copilot.AskAsync(systemPrompt + "\n\n" + userPrompt +
                "\n\nRespond conversationally. Do not emit JSON and do not claim that you created, changed, sent or scheduled anything.",
                profile.Current.CopilotWebSearchEnabled);
            return (T)(object)new GuardianActionResponse { Message = message };
        }
        if (Selection == "No AI") return default;
        if (Selection == "Microsoft AI (Entra)")
        {
            if (!IsMicrosoftConfigured) return default;
            var selectedSchema = GetSchema<T>();
            var selectedResponse = await AskMicrosoftAsync(systemPrompt, userPrompt, selectedSchema);
            return JsonSerializer.Deserialize<T>(CleanJson(selectedResponse), JsonOptions);
        }
        if (Selection == "OpenAI") return CreateStructuredProvider() is { } selectedOpenAi ? await selectedOpenAi.AskStructuredAsync<T>(systemPrompt, userPrompt) : default;
        if (IsMicrosoftConfigured)
        {
            try
            {
                var schema = GetSchema<T>();
                var response = await AskMicrosoftAsync(systemPrompt, userPrompt, schema);
                return JsonSerializer.Deserialize<T>(CleanJson(response), JsonOptions);
            }
            catch when (IsOpenAiConfigured) { }
        }
        var provider = CreateStructuredProvider();
        return provider is null ? default : await provider.AskStructuredAsync<T>(systemPrompt, userPrompt);
    }
    private OpenAIProvider? CreateProvider() { var configuration = CreateConfiguration(); return configuration is null ? null : new OpenAIProvider(configuration); }
    private OpenAIStructuredProvider? CreateStructuredProvider() { var configuration = CreateConfiguration(); return configuration is null ? null : new OpenAIStructuredProvider(configuration); }
    private IConfiguration? CreateConfiguration()
    {
        var key = secrets.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return string.IsNullOrWhiteSpace(key) ? null : new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = key }).Build();
    }

    private async Task<string> AskMicrosoftAsync(string systemPrompt, string userPrompt, (string Name, string Json)? schema = null)
    {
        var token = await microsoftAuthentication.GetTokenForScopesAsync(
            ["https://cognitiveservices.azure.com/.default"]);
        var endpoint = MicrosoftEndpoint.TrimEnd('/') + "/openai/v1/chat/completions";
        JsonNode payload = new JsonObject
        {
            ["model"] = MicrosoftDeployment,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            }
        };
        if (schema is not null)
        {
            payload["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schema.Value.Name,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(schema.Value.Json)
                }
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await MicrosoftClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft AI returned HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidOperationException("Microsoft AI returned an empty response.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string FirstConfigured(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string CleanJson(string value) => value.Trim().Trim('`').Replace("json\n", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    private static (string Name, string Json) GetSchema<T>() =>
        typeof(T) == typeof(Nekomata.AI.Models.Actions.ChaseConversationAdvice) ? (ChaseConversationAdviceSchema.Name, ChaseConversationAdviceSchema.Json) :
        typeof(T) == typeof(MeetingAnalysisResponse) ? (MeetingAnalysisSchema.Name, MeetingAnalysisSchema.Json) :
        typeof(T) == typeof(GuardianActionResponse) ? (GuardianConversationSchema.Name, GuardianConversationSchema.Json) :
        throw new NotSupportedException($"No schema has been registered for {typeof(T).Name}.");
}
