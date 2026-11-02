using System.Linq;

namespace Fort.ind_UWP
{
    public static class PathJoin
    {
        public static string Of(string root, params string[] parts)
        {
            var path = root ?? "";
            foreach (var child in parts.Select(part => (part ?? "").TrimStart('\\', '/')))
            {
                path = path.Length == 0 ? child : path.TrimEnd('\\', '/') + "\\" + child;
            }

            return path;
        }
    }
}
