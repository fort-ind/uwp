using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.UI.StartScreen;

namespace Fort.ind_UWP
{
    public static class GameTileService
    {
        private const int HashPrefixLength = 16;

        private static readonly Uri Square150x150Logo = new Uri("ms-appx:///Assets/Square150x150Logo.png");
        private static readonly Uri Wide310x150Logo = new Uri("ms-appx:///Assets/Wide310x150Logo.png");
        private static readonly Uri Square310x310Logo = new Uri("ms-appx:///Assets/Square310x310Logo.png");
        private static readonly Uri Square71x71Logo = new Uri("ms-appx:///Assets/SmallTile.png");

        public static bool CanPin(SearchItem game)
        {
            return game != null
                   && !string.IsNullOrWhiteSpace(game.Title)
                   && WebLauncher.TryCreateWebUri(game.Url) != null;
        }

        public static bool IsPinned(SearchItem game)
        {
            if (!CanPin(game)) return false;

            try
            {
                return SecondaryTile.Exists(TileIdFor(game.Url));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameTileService: could not check the tile for {game.Url} - {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> PinAsync(SearchItem game)
        {
            if (!CanPin(game)) return false;

            try
            {
                var tile = new SecondaryTile(TileIdFor(game.Url),
                                             game.Title,
                                             AppConstants.GameTileArgumentPrefix + game.Url,
                                             Square150x150Logo,
                                             TileSize.Default);

                var visuals = tile.VisualElements;
                visuals.Wide310x150Logo = Wide310x150Logo;
                visuals.Square310x310Logo = Square310x310Logo;
                visuals.Square71x71Logo = Square71x71Logo;
                visuals.ShowNameOnSquare150x150Logo = true;
                visuals.ShowNameOnWide310x150Logo = true;
                visuals.ShowNameOnSquare310x310Logo = true;

                return await tile.RequestCreateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameTileService: pinning {game.Url} failed - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> UnpinAsync(SearchItem game)
        {
            if (!CanPin(game)) return false;

            try
            {
                var tile = new SecondaryTile(TileIdFor(game.Url));
                return await tile.RequestDeleteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameTileService: unpinning {game.Url} failed - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static string ResolveGameUrl(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return null;
            if (!arguments.StartsWith(AppConstants.GameTileArgumentPrefix, StringComparison.Ordinal)) return null;

            var url = arguments.Substring(AppConstants.GameTileArgumentPrefix.Length);
            if (WebLauncher.TryCreateWebUri(url) == null)
            {
                Debug.WriteLine("GameTileService: ignoring a game tile argument that is not a web URL");
                return null;
            }

            return url;
        }

        public static async Task<bool> LaunchPinnedGameAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            var games = await SitemapService.LoadGameItemsAsync();
            var game = games.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.Ordinal));
            if (game == null)
            {
                Debug.WriteLine($"GameTileService: the pinned game is not in the sitemap - {url}");
                return false;
            }

            return await WebLauncher.LaunchAsync(game.Url);
        }

        private static string TileIdFor(string url)
        {
            var source = CryptographicBuffer.ConvertStringToBinary(url, BinaryStringEncoding.Utf8);
            var digest = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256).HashData(source);
            return AppConstants.GameTileIdPrefix + CryptographicBuffer.EncodeToHexString(digest).Substring(0, HashPrefixLength);
        }
    }
}
