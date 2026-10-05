using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialSuggestionList : UserControl
    {
        public const double ListWidth = 280;

        public const double RowHeight = 48;

        public SocialSuggestionList()
        {
            this.InitializeComponent();
        }

        public event EventHandler<SocialSuggestion> SuggestionClicked;

        public double MaxListHeight
        {
            get { return List.MaxHeight; }
        }

        public int Count
        {
            get { return List.Items.Count; }
        }

        public SocialSuggestion Selected
        {
            get { return List.SelectedItem as SocialSuggestion; }
        }

        public void Show(IReadOnlyList<SocialSuggestion> suggestions)
        {
            List.ItemsSource = suggestions;
            List.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
        }

        public void Clear()
        {
            List.ItemsSource = null;
        }

        public void Move(int step)
        {
            var count = List.Items.Count;
            if (count == 0) return;

            List.SelectedIndex = ((List.SelectedIndex + step) % count + count) % count;
            List.ScrollIntoView(List.SelectedItem);
        }

        private void List_ItemClick(object sender, ItemClickEventArgs e)
        {
            var suggestion = e.ClickedItem as SocialSuggestion;
            if (suggestion != null) SuggestionClicked?.Invoke(this, suggestion);
        }
    }
}
