using System.Net;
using System.Text.Json;

namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

/// <summary>Creates bounded Graph failures without retaining response bodies, request URLs or headers.</summary>
internal static class GraphResponseDiagnostics
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response,CancellationToken cancellationToken)
    {
        if(response.IsSuccessStatusCode)return;
        var code=await ReadSafeCodeAsync(response.Content,cancellationToken);
        throw new GraphRequestException(response.StatusCode,code);
    }

    private static async Task<string?> ReadSafeCodeAsync(HttpContent content,CancellationToken cancellationToken)
    {
        try
        {
            await using var stream=await content.ReadAsStreamAsync(cancellationToken);
            using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
            var root=document.RootElement;
            if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out var error))
            {
                if(error.ValueKind==JsonValueKind.Object&&error.TryGetProperty("code",out var nested)&&nested.ValueKind==JsonValueKind.String)return SafeCode(nested.GetString());
                if(error.ValueKind==JsonValueKind.String)return SafeCode(error.GetString());
            }
            if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("code",out var code)&&code.ValueKind==JsonValueKind.String)return SafeCode(code.GetString());
        }
        catch(JsonException){ }
        return null;
    }

    private static string? SafeCode(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=80&&
        value.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-' or '.')?value:null;
}

internal sealed class GraphRequestException : HttpRequestException
{
    public string? ErrorCode { get; }
    public GraphRequestException(HttpStatusCode statusCode,string? errorCode)
        :base($"{(int)statusCode} {statusCode}"+(errorCode is null?"":$" ({errorCode})"),null,statusCode)
    {
        ErrorCode=errorCode;
        if(errorCode is not null)Data["GraphErrorCode"]=errorCode;
    }
}
