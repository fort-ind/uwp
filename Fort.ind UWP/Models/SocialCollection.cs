using System.Collections.Generic;
using Windows.Data.Json;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public enum SocialCollectionKind
    {
        List,
        Antenna
    }

    public sealed class SocialCollection : BindableBase
    {
        private string _name;

        private bool _isPublic;

        private bool _hasUnread;

        private SocialCollection(SocialCollectionKind kind, string id, string name)
        {
            Kind = kind;
            Id = id;
            _name = name;
        }

        public SocialCollectionKind Kind { get; private set; }

        public string Id { get; private set; }

        public IReadOnlyList<string> UserIds { get; private set; } = SocialJson.EmptyList;

        public string Name
        {
            get { return _name; }
            private set
            {
                if (SetProperty(ref _name, value)) OnPropertyChanged("AutomationName");
            }
        }

        public bool IsPublic
        {
            get { return _isPublic; }
            private set { SetProperty(ref _isPublic, value); }
        }

        public bool HasUnread
        {
            get { return _hasUnread; }
            set
            {
                if (!SetProperty(ref _hasUnread, value)) return;

                OnPropertyChanged("UnreadVisibility");
                OnPropertyChanged("AutomationName");
            }
        }

        public Visibility UnreadVisibility
        {
            get { return _hasUnread ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string AutomationName
        {
            get
            {
                return _hasUnread ? LocalizedStrings.Format("SocialCollectionUnreadAutomationFormat", _name) : _name;
            }
        }

        public void Rename(string name, bool isPublic)
        {
            Name = name;
            IsPublic = isPublic;
        }

        public static SocialCollection ListFromJson(JsonObject obj)
        {
            var id = SocialJson.String(obj, "id");
            var name = SocialJson.String(obj, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(name)) return null;

            return new SocialCollection(SocialCollectionKind.List, id, name)
            {
                _isPublic = SocialJson.Bool(obj, "isPublic").GetValueOrDefault(),
                UserIds = SocialJson.StringList(obj, "userIds")
            };
        }

        public static SocialCollection AntennaFromJson(JsonObject obj)
        {
            var id = SocialJson.String(obj, "id");
            var name = SocialJson.String(obj, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(name)) return null;

            return new SocialCollection(SocialCollectionKind.Antenna, id, name)
            {
                _hasUnread = SocialJson.Bool(obj, "hasUnreadNote").GetValueOrDefault()
            };
        }

        public static IReadOnlyList<SocialCollection> AllFromJson(JsonArray array, SocialCollectionKind kind)
        {
            var collections = new List<SocialCollection>(array.Count);
            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var collection = kind == SocialCollectionKind.List
                                 ? ListFromJson(value.GetObject())
                                 : AntennaFromJson(value.GetObject());
                if (collection != null) collections.Add(collection);
            }

            return collections;
        }
    }
}
