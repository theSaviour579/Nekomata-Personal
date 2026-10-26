using System.Net;
using System.Text.RegularExpressions;

namespace Nekomata.Core.Diagnostics;

/// <summary>A safe integration-boundary failure that deliberately carries no response body or request URI.</summary>
public sealed partial class IntegrationObservationException : Exception
{
    public string IntegrationSource { get; }
    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }

    private IntegrationObservationException(string source,string originalType,HttpStatusCode? statusCode,string? errorCode)
        :base(BuildMessage(source,originalType,statusCode,errorCode))
    {
        IntegrationSource=source;
        StatusCode=statusCode;
        ErrorCode=errorCode;
    }

    public static IntegrationObservationException From(string source,Exception exception)
    {
        var http=Find<HttpRequestException>(exception);
        var status=http?.StatusCode;
        var code=FindCode(exception);
        if(code is null)
        {
            var match=ErrorCodePattern().Match(exception.Message??"");
            if(match.Success)code=SafeCode(match.Groups[1].Value);
        }
        return new(source,exception.GetType().Name,status,code);
    }

    private static string? FindCode(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
        {
            var code=SafeCode(current.Data["GraphErrorCode"]?.ToString())??SafeCode(current.Data["ErrorCode"]?.ToString());
            if(code is not null)return code;
        }
        return null;
    }

    private static T? Find<T>(Exception exception) where T:Exception
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if(current is T found)return found;
        return null;
    }

    private static string? SafeCode(string? value)=>!string.IsNullOrWhiteSpace(value)&&SafeCodePattern().IsMatch(value)
        ?value[..Math.Min(value.Length,80)]
        :null;

    private static string BuildMessage(string source,string originalType,HttpStatusCode? status,string? code)
    {
        var safeSource=DiagnosticTextSanitizer.Sanitize(source,80);
        var statusText=status is null?"failure":$"{(int)status.Value} {status.Value}";
        var codeText=code is null?"":$" ({code})";
        return $"{safeSource}: {statusText}{codeText} [{originalType}]";
    }

    [GeneratedRegex(@"(?i)(?:error[_ ]?code|code)\s*[:=]\s*[\""']?([A-Za-z][A-Za-z0-9_.-]{1,79})")]
    private static partial Regex ErrorCodePattern();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{1,79}$")]
    private static partial Regex SafeCodePattern();
}
