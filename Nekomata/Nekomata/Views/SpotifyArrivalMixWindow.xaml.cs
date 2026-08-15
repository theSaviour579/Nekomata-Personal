using Nekomata.UI.Services;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class SpotifyArrivalMixWindow : Window
{
    private readonly SpotifyPlaybackService _spotify;

    public SpotifyArrivalMixWindow(SpotifyPlaybackService spotify)
    {
        _spotify = spotify;
        InitializeComponent();
        PlaylistInput.Text = spotify.ArrivalPlaylistUri;
        Loaded += (_, _) => PlaylistInput.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _spotify.SetArrivalPlaylist(PlaylistInput.Text);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            ValidationText.Text = ex.Message;
            PlaylistInput.Focus();
        }
    }
}
