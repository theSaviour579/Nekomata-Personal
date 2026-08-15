namespace Nekomata.Integrations.Spotify;

public static class SpotifyPlaylistReference
{
    public static string Normalize(string value)
    {
        var input = value.Trim();
        if (input.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase) && input.Length > "spotify:playlist:".Length)
            return "spotify:playlist:" + input["spotify:playlist:".Length..].Split('?', '#')[0];
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0].Equals("playlist", StringComparison.OrdinalIgnoreCase))
                return "spotify:playlist:" + segments[1];
        }
        throw new ArgumentException("Enter a Spotify playlist link or a spotify:playlist URI.");
    }
}
