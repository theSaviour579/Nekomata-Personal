using System.Text.RegularExpressions;

namespace Nekomata.Core.Diagnostics;

public static partial class DiagnosticTextSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = SecretAssignmentPattern().Replace(value, "$1=[redacted]");
        sanitized = BearerPattern().Replace(sanitized, "Bearer [redacted]");
        sanitized = UriCredentialsPattern().Replace(sanitized, "$1[redacted]@");
        return sanitized.Length <= 500 ? sanitized : sanitized[..500] + "…";
    }

    [GeneratedRegex(@"(?i)\b(password|token|api[-_ ]?key|client[-_ ]?secret)\s*[=:]\s*[^\s;&,]+")]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(https?://)[^/@\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UriCredentialsPattern();
}
