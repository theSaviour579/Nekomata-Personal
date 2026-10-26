using Nekomata.Integrations.MicrosoftGraph.Meetings;
using Xunit;

namespace Nekomata.Tests;

public sealed class TeamsTranscriptVttTests
{
    [Fact]
    public void FromVtt_ProducesSpeakerLinesAndMergesRuns()
    {
        const string vtt = """
            WEBVTT

            0b5c7d1e-1/12-0
            00:00:01.000 --> 00:00:04.000
            <v Sam Wells>Morning all.</v>

            0b5c7d1e-1/13-0
            00:00:04.000 --> 00:00:07.000
            <v Sam Wells>Can we agree the PP data by Thursday?</v>

            00:00:07.500 --> 00:00:09.000
            <v Alex Morgan>Yes, I'll send it Thursday.</v>
            """;
        Assert.Equal("Sam Wells: Morning all. Can we agree the PP data by Thursday?\nAlex Morgan: Yes, I'll send it Thursday.",
            TeamsTranscriptService.FromVtt(vtt).ReplaceLineEndings("\n"));
    }
}
