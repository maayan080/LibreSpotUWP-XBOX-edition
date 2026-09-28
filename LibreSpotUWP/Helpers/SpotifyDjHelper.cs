using SpotifyAPI.Web;
using System;

namespace LibreSpotUWP.Helpers
{
    public static class SpotifyDjHelper
    {
        private const string SpotifyDjPlaylistId = "37i9dQZF1EYkqdzj48dyYq";

        public static bool IsHomeDjPlaylist(FullPlaylist playlist)
        {
            if (playlist == null)
                return false;

            return string.Equals(playlist.Id, SpotifyDjPlaylistId, StringComparison.OrdinalIgnoreCase) ||
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
    }
}
