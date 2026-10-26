using System.Text.RegularExpressions;

namespace Nekomata.Core.Diagnostics;

public static partial class DiagnosticTextSanitizer
{
    public static string Sanitize(string? value, int maximumLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (maximumLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));

        var sanitized = ConnectionStringPattern().Replace(value, "$1=[redacted]");
        sanitized = CredentialTargetPattern().Replace(sanitized, "$1=[redacted]");
        sanitized = SecretAssignmentPattern().Replace(sanitized, "$1=[redacted]");
        sanitized = BearerPattern().Replace(sanitized, "Bearer [redacted]");
        sanitized = UriCredentialsPattern().Replace(sanitized, "$1[redacted]@");
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength] + "…";
    }

    [GeneratedRegex(@"(?i)\b(password|passphrase|token|api[-_ ]?key|client[-_ ]?secret|credential(?:\s+value|\s+failed)?|(?:encrypted\s+)?payload|ciphertext)\s*(?:is|[=:])\s*(?:""[^""]*""|'[^']*'|[^\s;&,]+)")]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"(?i)\b(connection\s*string|connectionstring)\s*(?:is|[=:])\s*(?:""[^""]*""|'[^']*'|[^\r\n]+)")]
    private static partial Regex ConnectionStringPattern();

    [GeneratedRegex(@"(?i)\b(credential(?:\s+manager)?\s+target|credentialtarget)\s*(?:is|[=:])\s*(?:""[^""]*""|'[^']*'|[^,;\r\n]+)")]
    private static partial Regex CredentialTargetPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(https?://)[^/@\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UriCredentialsPattern();
}
