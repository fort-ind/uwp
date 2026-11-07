using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialPolicies
    {
        public const string PoliciesField = "policies";

        private static readonly object s_lock = new object();

        private static readonly SocialPolicies s_none = new SocialPolicies(null);

        private static string s_source;

        private static SocialPolicies s_parsed = s_none;

        private readonly JObject _values;

        private SocialPolicies(JObject values)
        {
            _values = values;
        }

        public static SocialPolicies Current
        {
            get { return FromProfile(ProfileService.CurrentUser); }
        }

        public static SocialPolicies FromProfile(UserProfile profile)
        {
            var source = profile == null ? null : profile.DetailJson;
            if (string.IsNullOrEmpty(source)) return s_none;

            lock (s_lock)
            {
                if (ReferenceEquals(source, s_source) || string.Equals(source, s_source, System.StringComparison.Ordinal)) return s_parsed;
            }

            JObject obj;
            var parsed = s_none;
            if (JsonText.TryParseObject(source, out obj))
            {
                var values = SocialJson.Object(obj, PoliciesField);
                if (values != null) parsed = new SocialPolicies(values);
            }

            lock (s_lock)
            {
                s_source = source;
                s_parsed = parsed;
            }

            return parsed;
        }

        public bool IsKnown
        {
            get { return _values != null; }
        }

        public bool AllowsPublicNotes
        {
            get { return Flag("canPublicNote"); }
        }

        public bool AllowsNoteSearch
        {
            get { return Flag("canSearchNotes"); }
        }

        public bool AllowsGlobalTimeline
        {
            get { return Flag("gtlAvailable"); }
        }

        public bool AllowsLocalTimeline
        {
            get { return Flag("ltlAvailable"); }
        }

        public int? ScheduledNoteMax
        {
            get { return Limit("scheduleNoteMax"); }
        }

        public int? MaxFileSizeMb
        {
            get { return Limit("maxFileSizeMb"); }
        }

        public ulong UploadLimitBytes
        {
            get
            {
                var megabytes = MaxFileSizeMb;
                return megabytes.HasValue && megabytes.Value > 0
                       ? (ulong)megabytes.Value * 1024 * 1024
                       : AppConstants.SocialUploadLimitBytes;
            }
        }

        public ulong UploadLimitMegabytes
        {
            get { return UploadLimitBytes / (1024 * 1024); }
        }

        public int? PinLimit
        {
            get { return Limit("pinLimit"); }
        }

        public int? ListLimit
        {
            get { return Limit("userListLimit"); }
        }

        public int? ListMemberLimit
        {
            get { return Limit("userEachUserListsLimit"); }
        }

        public int? MentionLimit
        {
            get { return Limit("mentionLimit"); }
        }

        private bool Flag(string key)
        {
            return _values == null || SocialJson.Bool(_values, key).GetValueOrDefault(true);
        }

        private int? Limit(string key)
        {
            if (_values == null) return null;

            var value = SocialJson.Int(_values, key);
            return value.HasValue && value.Value >= 0 ? value : null;
        }
    }
}
