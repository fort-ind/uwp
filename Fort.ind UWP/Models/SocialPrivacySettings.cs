using System;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialPrivacySettings
    {
        public const string LockedField = "isLocked";
        public const string AutoAcceptFollowedField = "autoAcceptFollowed";
        public const string FollowingVisibilityField = "followingVisibility";
        public const string FollowersVisibilityField = "followersVisibility";
        public const string PublicReactionsField = "publicReactions";
        public const string HideOnlineStatusField = "hideOnlineStatus";
        public const string ExplorableField = "isExplorable";
        public const string NoCrawleField = "noCrawle";
        public const string PreventAiLearningField = "preventAiLearning";
        public const string ChatScopeField = "chatScope";

        private SocialPrivacySettings()
        {
        }

        public bool IsLocked { get; private set; }

        public bool AutoAcceptFollowed { get; private set; }

        public string FollowingVisibility { get; private set; }

        public string FollowersVisibility { get; private set; }

        public bool PublicReactions { get; private set; }

        public bool HideOnlineStatus { get; private set; }

        public bool IsExplorable { get; private set; }

        public bool NoCrawle { get; private set; }

        public bool PreventAiLearning { get; private set; }

        public string ChatScope { get; private set; }

        public static SocialPrivacySettings FromProfile(UserProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId)) return null;

            JsonObject obj;
            if (string.IsNullOrEmpty(profile.DetailJson) || !JsonObject.TryParse(profile.DetailJson, out obj))
            {
                obj = new JsonObject();
            }

            return new SocialPrivacySettings
            {
                IsLocked = SocialJson.Bool(obj, LockedField).GetValueOrDefault(),
                AutoAcceptFollowed = SocialJson.Bool(obj, AutoAcceptFollowedField).GetValueOrDefault(true),
                FollowingVisibility = SocialJson.String(obj, FollowingVisibilityField) ?? "public",
                FollowersVisibility = SocialJson.String(obj, FollowersVisibilityField) ?? "public",
                PublicReactions = SocialJson.Bool(obj, PublicReactionsField).GetValueOrDefault(),
                HideOnlineStatus = SocialJson.Bool(obj, HideOnlineStatusField).GetValueOrDefault(),
                IsExplorable = SocialJson.Bool(obj, ExplorableField).GetValueOrDefault(true),
                NoCrawle = SocialJson.Bool(obj, NoCrawleField).GetValueOrDefault(),
                PreventAiLearning = SocialJson.Bool(obj, PreventAiLearningField).GetValueOrDefault(),
                ChatScope = SocialJson.String(obj, ChatScopeField)
            };
        }

        public bool FlagValue(string field)
        {
            switch (field)
            {
                case LockedField: return IsLocked;
                case AutoAcceptFollowedField: return AutoAcceptFollowed;
                case PublicReactionsField: return PublicReactions;
                case HideOnlineStatusField: return HideOnlineStatus;
                case ExplorableField: return IsExplorable;
                case NoCrawleField: return NoCrawle;
                case PreventAiLearningField: return PreventAiLearning;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        public string ChoiceValue(string field)
        {
            switch (field)
            {
                case FollowingVisibilityField: return FollowingVisibility;
                case FollowersVisibilityField: return FollowersVisibility;
                case ChatScopeField: return ChatScope;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }
    }
}
