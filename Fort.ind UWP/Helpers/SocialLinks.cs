using System;

namespace Fort.ind_UWP
{
    public static class SocialLinks
    {
        public static string InstanceUrl(string path)
        {
            return "https://" + MisskeyAuthService.InstanceHost + path;
        }

        public static string NoteUrl(string noteId)
        {
            if (string.IsNullOrWhiteSpace(noteId)) return null;
            return InstanceUrl("/notes/" + Uri.EscapeDataString(noteId));
        }

        public static string UserUrl(SocialUser user)
        {
            return user == null ? null : UserUrl(user.Username, user.Host);
        }

        public static string UserUrl(string username, string host)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            var handle = "/@" + Uri.EscapeDataString(username);
            if (!string.IsNullOrWhiteSpace(host) && Uri.CheckHostName(host) != UriHostNameType.Unknown)
            {
                handle += "@" + host;
            }

            return InstanceUrl(handle);
        }

        public static string ProfileSettingsUrl()
        {
            return InstanceUrl("/settings/profile");
        }

        public static string TagUrl(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            return InstanceUrl("/tags/" + Uri.EscapeDataString(tag));
        }

        public static Uri StaticEmojiUri(string source)
        {
            var sourceUri = WebLauncher.TryCreateWebUri(source);
            if (sourceUri == null) return null;

            return WebLauncher.TryCreateFetchUri(InstanceUrl("/proxy/emoji.webp?url=" + Uri.EscapeDataString(sourceUri.AbsoluteUri) + "&emoji=1&static=1"));
        }

        public static string FormatHandle(string username, string host)
        {
            if (string.IsNullOrWhiteSpace(username)) return "";
            return string.IsNullOrWhiteSpace(host) ? "@" + username : "@" + username + "@" + host;
        }
    }
}
