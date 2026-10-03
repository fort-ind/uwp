using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialLinkPreview
    {
        private SocialLinkPreview()
        {
        }

        public string Url { get; private set; }

        public string Title { get; private set; }

        public string Description { get; private set; }

        public string SiteName { get; private set; }

        public string ThumbnailUrl { get; private set; }

        public bool IsSensitive { get; private set; }

        internal static SocialLinkPreview FromJson(JsonObject obj, string requestedUrl)
        {
            if (obj == null) return null;

            var title = SocialJson.String(obj, "title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var url = SocialJson.String(obj, "url");

            return new SocialLinkPreview
            {
                Url = WebLauncher.TryCreateWebUri(url) != null ? url : requestedUrl,
                Title = title.Trim(),
                Description = SocialJson.String(obj, "description"),
                SiteName = SocialJson.String(obj, "sitename"),
                ThumbnailUrl = SocialJson.String(obj, "thumbnail"),
                IsSensitive = SocialJson.Bool(obj, "sensitive").GetValueOrDefault()
            };
        }
    }
}
