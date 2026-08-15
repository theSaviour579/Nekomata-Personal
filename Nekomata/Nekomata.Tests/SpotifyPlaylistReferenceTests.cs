using Nekomata.Integrations.Spotify;
using Xunit;

namespace Nekomata.Tests;

public sealed class SpotifyPlaylistReferenceTests
{
    [Theory]
    [InlineData("https://open.spotify.com/playlist/abc123?si=sample", "spotify:playlist:abc123")]
    [InlineData("spotify:playlist:xyz789", "spotify:playlist:xyz789")]
    public void Normalize_AcceptsSpotifyPlaylistFormats(string input, string expected) =>
        Assert.Equal(expected, SpotifyPlaylistReference.Normalize(input));

    [Fact]
    public void Normalize_RejectsNonPlaylistLinks() =>
        Assert.Throws<ArgumentException>(() => SpotifyPlaylistReference.Normalize("https://example.com/music"));
}
