using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianSpeechTextNormalizerTests
{
    [Theory]
    [InlineData("YoY Spend Deep Dive", "year on year Spend Deep Dive")]
    [InlineData("Estimated 3h 0m", "Estimated 3 hours 0 minutes")]
    [InlineData("Remaining 1h 1m", "Remaining 1 hour 1 minute")]
    [InlineData("P1 SLA breach on #5739", "priority one S L A breach on ticket 5739")]
    [InlineData("Open ticket #5739", "Open ticket 5739")]
    [InlineData("SQL KPI and ROI", "S Q L K P I and return on investment")]
    public void Expands_business_terms_for_natural_speech(string input, string expected)
    {
        Assert.Equal(expected, GuardianSpeechTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Expands_clock_duration_and_currency()
    {
        var result = GuardianSpeechTextNormalizer.Normalize("03:02:01 and \u00A322,000");

        Assert.Equal("3 hours, 2 minutes, and 1 second and 22,000 pounds", result);
    }
}