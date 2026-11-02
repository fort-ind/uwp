using System;
using System.Collections.Generic;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialAccountSettings
    {
        public const string MutedWordsField = "mutedWords";
        public const string HardMutedWordsField = "hardMutedWords";
        public const string MutedInstancesField = "mutedInstances";
        public const string NotificationConfigField = "notificationRecieveConfig";
        public const string DefaultCwField = "defaultCW";
        public const string DefaultSensitiveField = "defaultSensitive";
        public const string AlwaysMarkNsfwField = "alwaysMarkNsfw";
        public const string AutoSensitiveField = "autoSensitive";
        public const string LangField = "lang";
        public const string FollowedMessageField = "followedMessage";
        public const string RequireSigninField = "requireSigninToViewContents";
        public const string EnableRssField = "enableRss";
        public const string NoIndexField = "noindex";
        public const string CarefulBotField = "carefulBot";
        public const string NotesFollowersOnlyBeforeField = "makeNotesFollowersOnlyBefore";
        public const string NotesHiddenBeforeField = "makeNotesHiddenBefore";

        public const int FollowedMessageMaxLength = 256;

        private SocialAccountSettings()
        {
        }

        public SocialMuteWordSet MutedWords { get; private set; }

        public SocialMuteWordSet HardMutedWords { get; private set; }

        public IReadOnlyList<string> MutedInstances { get; private set; }

        public string DefaultCw { get; private set; }

        public bool DefaultSensitive { get; private set; }

        public bool AlwaysMarkNsfw { get; private set; }

        public bool AutoSensitive { get; private set; }

        public string Lang { get; private set; }

        public string FollowedMessage { get; private set; }

        public bool RequireSignin { get; private set; }

        public bool EnableRss { get; private set; }

        public bool NoIndex { get; private set; }

        public bool CarefulBot { get; private set; }

        public int? NotesFollowersOnlyBefore { get; private set; }

        public int? NotesHiddenBefore { get; private set; }

        public string NotificationConfigJson { get; private set; }

        public static SocialAccountSettings FromProfile(UserProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId)) return null;

            JsonObject obj;
            if (string.IsNullOrEmpty(profile.DetailJson) || !JsonObject.TryParse(profile.DetailJson, out obj))
            {
                obj = new JsonObject();
            }

            var config = SocialJson.Object(obj, NotificationConfigField);
            return new SocialAccountSettings
            {
                MutedWords = SocialMuteWordSet.FromJson(SocialJson.Array(obj, MutedWordsField)),
                HardMutedWords = SocialMuteWordSet.FromJson(SocialJson.Array(obj, HardMutedWordsField)),
                MutedInstances = SocialJson.StringList(obj, MutedInstancesField),
                DefaultCw = SocialJson.String(obj, DefaultCwField),
                DefaultSensitive = SocialJson.Bool(obj, DefaultSensitiveField).GetValueOrDefault(),
                AlwaysMarkNsfw = SocialJson.Bool(obj, AlwaysMarkNsfwField).GetValueOrDefault(),
                AutoSensitive = SocialJson.Bool(obj, AutoSensitiveField).GetValueOrDefault(),
                Lang = SocialJson.String(obj, LangField),
                FollowedMessage = SocialJson.String(obj, FollowedMessageField),
                RequireSignin = SocialJson.Bool(obj, RequireSigninField).GetValueOrDefault(),
                EnableRss = SocialJson.Bool(obj, EnableRssField).GetValueOrDefault(),
                NoIndex = SocialJson.Bool(obj, NoIndexField).GetValueOrDefault(),
                CarefulBot = SocialJson.Bool(obj, CarefulBotField).GetValueOrDefault(),
                NotesFollowersOnlyBefore = SocialJson.Int(obj, NotesFollowersOnlyBeforeField),
                NotesHiddenBefore = SocialJson.Int(obj, NotesHiddenBeforeField),
                NotificationConfigJson = config == null ? null : config.Stringify()
            };
        }

        public bool FlagValue(string field)
        {
            switch (field)
            {
                case DefaultSensitiveField: return DefaultSensitive;
                case AlwaysMarkNsfwField: return AlwaysMarkNsfw;
                case AutoSensitiveField: return AutoSensitive;
                case RequireSigninField: return RequireSignin;
                case EnableRssField: return EnableRss;
                case NoIndexField: return NoIndex;
                case CarefulBotField: return CarefulBot;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        public string NotificationRule(string notificationType)
        {
            JsonObject config;
            if (string.IsNullOrEmpty(NotificationConfigJson) || !JsonObject.TryParse(NotificationConfigJson, out config)) return SocialNotificationRules.All;

            var rule = SocialJson.Object(config, notificationType);
            return SocialJson.String(rule, "type") ?? SocialNotificationRules.All;
        }

        public JsonObject NotificationConfigWith(string notificationType, string rule)
        {
            JsonObject config;
            if (string.IsNullOrEmpty(NotificationConfigJson) || !JsonObject.TryParse(NotificationConfigJson, out config))
            {
                config = new JsonObject();
            }

            var entry = new JsonObject();
            entry.SetNamedValue("type", JsonValue.CreateStringValue(rule));
            config.SetNamedValue(notificationType, entry);
            return config;
        }
    }

    public static class SocialNotificationRules
    {
        public const string All = "all";
        public const string Following = "following";
        public const string Follower = "follower";
        public const string MutualFollow = "mutualFollow";
        public const string FollowingOrFollower = "followingOrFollower";
        public const string Never = "never";
        public const string List = "list";

        public static readonly string[] Types =
        {
            "follow", "mention", "reply", "renote", "quote", "reaction", "pollEnded", "receiveFollowRequest"
        };
    }
}
