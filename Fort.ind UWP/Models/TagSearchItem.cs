namespace Fort.ind_UWP
{
    public sealed class TagSearchItem : SearchItem
    {
        public TagSearchItem(string tag, string title)
            : base(title, AppConstants.CategorySocial, AppConstants.NavigationSocial)
        {
            Tag = tag;
            Icon = "";
        }

        public string Tag { get; private set; }
    }
}
