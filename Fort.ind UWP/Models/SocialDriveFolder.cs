using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialDriveFolder
    {
        private static readonly IReadOnlyList<SocialDriveFolder> s_none = new SocialDriveFolder[0];

        private SocialDriveFolder()
        {
        }

        public string Id { get; private set; }

        public string Name { get; private set; }

        public string AutomationName
        {
            get { return LocalizedStrings.Format("SocialDriveFolderAutomationFormat", Name); }
        }

        internal static SocialDriveFolder FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            return new SocialDriveFolder
            {
                Id = id,
                Name = SocialJson.String(obj, "name") ?? ""
            };
        }

        internal static IReadOnlyList<SocialDriveFolder> ListFromJson(JArray array)
        {
            if (array == null || array.Count == 0) return s_none;

            var folders = new List<SocialDriveFolder>();
            folders.AddRange(array.OfType<JObject>().Select(value => FromJson(value)).Where(folder => folder != null));

            return folders.Count == 0 ? s_none : folders;
        }
    }
}
