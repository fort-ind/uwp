namespace Fort.ind_UWP
{
    public sealed class NoteSearchItem : SearchItem
    {
        public NoteSearchItem(string query, string title)
            : base(title, AppConstants.CategorySocial, AppConstants.NavigationSocial)
        {
            Query = query;
            Icon = "\uE721";
        }

        public string Query { get; private set; }
    }
}
