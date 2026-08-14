using Nekomata.Core.Diagnostics;
using Xunit;

namespace Nekomata.Tests;

public sealed class DiagnosticTextSanitizerTests
{
    [Theory]
    [InlineData("password=super-secret", "super-secret")]
    [InlineData("api_key: abc123", "abc123")]
    [InlineData("client-secret=xyz789", "xyz789")]
    [InlineData("Authorization failed for Bearer gho_exampletoken", "gho_exampletoken")]
    [InlineData("https://user:secret@example.test/path", "user:secret")]
    public void Sanitizer_removes_credentials(string input, string secret)
    {
        var sanitized = DiagnosticTextSanitizer.Sanitize(input);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains("redacted", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitizer_preserves_useful_error_context()
    {
        var sanitized = DiagnosticTextSanitizer.Sanitize(
            "Request timed out while contacting api.example.test; token=hidden");

        Assert.Contains("Request timed out", sanitized);
        Assert.Contains("api.example.test", sanitized);
    }
}
