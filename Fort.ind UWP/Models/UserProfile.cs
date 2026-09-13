using System;
using System.Runtime.Serialization;

namespace Fort.ind_UWP
{
    [DataContract]
    public class UserProfile
    {
        [DataMember]
        public string UserId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string Host { get; set; }

        [DataMember]
        public string DisplayName { get; set; }

        [DataMember]
        public string Bio { get; set; }

        [DataMember]
        public string AvatarUrl { get; set; }

        [DataMember]
        public DateTime CreatedDate { get; set; }

        [DataMember]
        public DateTime LastLoginDate { get; set; }

        [DataMember]
        public UserPreferences Preferences
        {
            get
            {
                if (_preferences == null) _preferences = new UserPreferences();
                return _preferences;
            }
            set
            {
                _preferences = value;
            }
        }
        private UserPreferences _preferences;

        /// <summary>
        /// "No date" for <see cref="CreatedDate"/> and <see cref="LastLoginDate"/>: DateTime.MinValue
        /// with Kind Utc.
        /// </summary>
        /// <remarks>
        /// The Kind is the point. DataContractJsonSerializer converts any non-Utc DateTime to UTC
        /// before writing it, and default(DateTime) is Unspecified, which it treats as local time -
        /// so east of UTC, MinValue minus the offset falls below the representable range and the
        /// save throws. SaveProfileAsync swallowed that, so an account whose createdAt did not parse
        /// was never cached at all in those time zones. Utc MinValue writes as-is, and still compares
        /// equal to DateTime.MinValue, which is all ProfilePage's "is it set" checks look at.
        /// </remarks>
        public static readonly DateTime UnsetDate = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        public UserProfile()
        {
            Preferences = new UserPreferences();
            CreatedDate = UnsetDate;
            LastLoginDate = UnsetDate;
        }

        public UserProfile Clone()
        {
            UserProfile copy = new UserProfile()
            {
                UserId = this.UserId,
                Username = this.Username,
                Host = this.Host,
                DisplayName = this.DisplayName,
                Bio = this.Bio,
                AvatarUrl = this.AvatarUrl,
                CreatedDate = this.CreatedDate,
                LastLoginDate = this.LastLoginDate
            };
            var prefs = this.Preferences;
            copy.Preferences = new UserPreferences()
            {
                EnableLiveTile = prefs.EnableLiveTile,
                EnableNotifications = prefs.EnableNotifications,
                Theme = prefs.Theme
            };
            return copy;
        }
    }

    [DataContract]
    public class UserPreferences
    {
        [DataMember]
        public bool EnableLiveTile { get; set; } = true;

        [DataMember]
        public bool EnableNotifications { get; set; } = true;

        [DataMember]
        public string Theme { get; set; } = "Dark";
    }
}
