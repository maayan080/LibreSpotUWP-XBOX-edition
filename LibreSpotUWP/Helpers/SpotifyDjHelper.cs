using SpotifyAPI.Web;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreSpotUWP.Interfaces;
using LibreSpotUWP.Services;

namespace LibreSpotUWP.Helpers
{
    public static class SpotifyDjHelper
    {
        public const string HomeDjPlaylistId = "37i9dQZF1EYkqdzj48dyYq";
        private static int _startingHomeDj;

        public static bool IsHomeDjPlaylist(FullPlaylist playlist)
        {
            if (playlist == null)
                return false;

            return string.Equals(playlist.Id, HomeDjPlaylistId, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(playlist.Name, "DJ", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(playlist.Owner?.Id, "spotify", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetPlaylistUri(FullPlaylist playlist)
        {
            if (playlist == null)
                return null;

            return !string.IsNullOrWhiteSpace(playlist.Uri)
                ? playlist.Uri
                : $"spotify:playlist:{playlist.Id}";
        }

        public static bool IsHomeDjPlaylistUri(string uri)
        {
            return string.Equals(
                uri,
                $"spotify:playlist:{HomeDjPlaylistId}",
                StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<bool> StartPlaybackIfDjAsync(FullPlaylist playlist, IAppShell shell)
        {
            if (!IsHomeDjPlaylist(playlist))
                return false;

            if (App.Media != null)
            {
                try
                {
                    var artworkUri = playlist.Images?.FirstOrDefault()?.Url;
                    await App.Media.PlaySpotifyDjAsync(GetPlaylistUri(playlist), artworkUri);
                }
                catch (Exception ex)
                {
                    LogService.Warn($"[SpotifyDjHelper.StartPlaybackIfDjAsync] Unable to start Spotify DJ: {ex.Message}");
                    return true;
                }
            }

            shell?.NavigateTo("Player", true);
            return true;
        }

        public static async Task StartHomeDjAsync(IAppShell shell)
        {
            if (Interlocked.Exchange(ref _startingHomeDj, 1) != 0)
                return;

            try
            {
                FullPlaylist playlist = null;
                try
                {
                    playlist = (await App.SpotifyWeb.GetPlaylistAsync(HomeDjPlaylistId, true))?.Value;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"[SpotifyDjHelper.StartHomeDjAsync] Unable to load DJ playlist details: {ex.Message}");
                }

                if (playlist == null)
                {
                    playlist = new FullPlaylist
                    {
                        Id = HomeDjPlaylistId,
                        Uri = $"spotify:playlist:{HomeDjPlaylistId}",
                        Name = "DJ"
                    };
                }
                await StartPlaybackIfDjAsync(playlist, shell);
            }
            finally
            {
                Volatile.Write(ref _startingHomeDj, 0);
            }
        }
    }
}
