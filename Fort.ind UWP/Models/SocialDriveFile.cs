using System;
using System.Collections.Generic;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public enum SocialDriveFileKind
    {
        Image,
        Gif,
        Video,
        Audio,
        Other
    }

    public sealed class SocialDriveFile
    {
        private static readonly IReadOnlyList<SocialDriveFile> s_none = new SocialDriveFile[0];

        private SocialDriveFile()
        {
        }

        public string Id { get; private set; }

        public string Type { get; private set; }

        public string Name { get; private set; }

        public SocialDriveFileKind Kind { get; private set; }

        public bool IsSensitive { get; private set; }

        public string Blurhash { get; private set; }

        public int? Width { get; private set; }

        public int? Height { get; private set; }

        public string Url { get; private set; }

        public string ThumbnailUrl { get; private set; }

        public string Comment { get; private set; }

        public bool IsVisual
        {
            get { return Kind == SocialDriveFileKind.Image || Kind == SocialDriveFileKind.Gif || Kind == SocialDriveFileKind.Video; }
        }

        internal static SocialDriveFile FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            var url = SocialJson.String(obj, "url");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) return null;

            var type = SocialJson.String(obj, "type") ?? "";
            var properties = SocialJson.Object(obj, "properties");

            return new SocialDriveFile
            {
                Id = id,
                Type = type,
                Name = SocialJson.String(obj, "name"),
                Kind = KindFor(type),
                IsSensitive = SocialJson.Bool(obj, "isSensitive").GetValueOrDefault(),
                Blurhash = SocialJson.String(obj, "blurhash"),
                Width = PositiveOrNull(SocialJson.Int(properties, "width")),
                Height = PositiveOrNull(SocialJson.Int(properties, "height")),
                Url = url,
                ThumbnailUrl = SocialJson.String(obj, "thumbnailUrl"),
                Comment = SocialJson.String(obj, "comment")
            };
        }

        internal JsonObject ToJson()
        {
            var obj = new JsonObject();
            SocialJson.Put(obj, "id", Id);
            SocialJson.Put(obj, "type", Type);
            SocialJson.Put(obj, "name", Name);
            obj.Add("isSensitive", JsonValue.CreateBooleanValue(IsSensitive));
            SocialJson.Put(obj, "blurhash", Blurhash);
            SocialJson.Put(obj, "url", Url);
            SocialJson.Put(obj, "thumbnailUrl", ThumbnailUrl);
            SocialJson.Put(obj, "comment", Comment);

            var properties = new JsonObject();
            if (Width.HasValue) properties.Add("width", JsonValue.CreateNumberValue(Width.Value));
            if (Height.HasValue) properties.Add("height", JsonValue.CreateNumberValue(Height.Value));
            obj.Add("properties", properties);
            return obj;
        }

        internal static IReadOnlyList<SocialDriveFile> ListFromJson(JsonArray array)
        {
            if (array == null || array.Count == 0) return s_none;

            var files = new List<SocialDriveFile>();
            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var file = FromJson(value.GetObject());
                if (file != null) files.Add(file);
            }

            return files.Count == 0 ? s_none : files;
        }

        private static SocialDriveFileKind KindFor(string type)
        {
            if (string.Equals(type, "image/gif", StringComparison.OrdinalIgnoreCase)) return SocialDriveFileKind.Gif;
            if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return SocialDriveFileKind.Image;
            if (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return SocialDriveFileKind.Video;
            if (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return SocialDriveFileKind.Audio;
            return SocialDriveFileKind.Other;
        }

        private static int? PositiveOrNull(int? value)
        {
            return value.HasValue && value.Value > 0 ? value : null;
        }
    }
}
