using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage
    {
        private static readonly IReadOnlyList<SocialCollection> NoCollections = new SocialCollection[0];

        private static SocialCollectionKind LastCollectionKind { get; set; } = SocialCollectionKind.List;

        private static string LastListId { get; set; }

        private static string LastAntennaId { get; set; }

        private IReadOnlyList<SocialCollection> _lists = NoCollections;

        private IReadOnlyList<SocialCollection> _antennas = NoCollections;

        private SocialCollectionKind _collectionKind = SocialCollectionKind.List;

        private bool _changingCollection;

        private int _antennaFlagsVersion;

        private IReadOnlyList<SocialCollection> CurrentCollections()
        {
            return _collectionKind == SocialCollectionKind.List ? _lists : _antennas;
        }

        private void ApplyCollectionKind(SocialCollectionKind kind)
        {
            _collectionKind = kind;

            var tab = kind == SocialCollectionKind.Antenna ? AntennasTab : ListsTab;
            if (!tab.IsChecked.GetValueOrDefault()) tab.IsChecked = true;

            var manage = kind == SocialCollectionKind.List ? Visibility.Visible : Visibility.Collapsed;
            CollectionNewButton.Visibility = manage;
            CollectionMoreButton.Visibility = manage;
        }

        private void CollectionsTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_tabsReady) return;

            var kind = ReferenceEquals(sender, AntennasTab) ? SocialCollectionKind.Antenna : SocialCollectionKind.List;
            if (kind == _collectionKind) return;

            LastCollectionKind = kind;
            ApplyCollectionKind(kind);
            ShowPickerForKind();
        }

        private void CollectionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_changingCollection) return;

            ShowCollection(CollectionPicker.SelectedItem as SocialCollection);
        }

        private SocialCollection PreferredCollection(IReadOnlyList<SocialCollection> source)
        {
            var wanted = _collectionKind == SocialCollectionKind.List ? LastListId : LastAntennaId;
            return source.FirstOrDefault(collection => string.Equals(collection.Id, wanted, StringComparison.Ordinal))
                   ?? source.FirstOrDefault();
        }

        private void ShowPickerForKind()
        {
            var source = CurrentCollections();

            _changingCollection = true;
            try
            {
                CollectionPicker.ItemsSource = source;
                CollectionPicker.SelectedItem = PreferredCollection(source);
            }
            finally
            {
                _changingCollection = false;
            }

            CollectionPicker.IsEnabled = source.Count > 0;
            ShowCollection(CollectionPicker.SelectedItem as SocialCollection);
        }

        private void ShowCollection(SocialCollection collection)
        {
            var feed = _collections;
            feed.Version++;
            feed.Requested = true;
            feed.Items.ReplaceAll(new SocialNoteItem[0], false);
            CollectionMoreButton.IsEnabled = collection != null && collection.Kind == SocialCollectionKind.List;

            if (collection == null)
            {
                var lists = _collectionKind == SocialCollectionKind.List;
                feed.SourceId = null;
                feed.Timeline = lists ? SocialTimeline.List : SocialTimeline.Antenna;
                feed.EmptyKey = lists ? "SocialListsNone" : "SocialAntennasNone";
                SetTimelineState(feed, FeedState.Empty);

                feed.Button.Content = LocalizedStrings.Get("SocialListCreateButton");
                feed.Button.Visibility = lists ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            if (collection.Kind == SocialCollectionKind.List) LastListId = collection.Id;
            else LastAntennaId = collection.Id;

            feed.Timeline = collection.Kind == SocialCollectionKind.List ? SocialTimeline.List : SocialTimeline.Antenna;
            feed.SourceId = collection.Id;
            feed.EmptyKey = collection.Kind == SocialCollectionKind.List ? "SocialListEmpty" : "SocialAntennaEmpty";
            LoadTimeline(feed);
        }

        private async Task<bool> LoadCollectionsAsync(TimelineFeed feed, bool keepList)
        {
            var version = ++feed.Version;
            feed.Requested = true;
            if (!keepList) SetTimelineState(feed, FeedState.Loading);

            try
            {
                var listsTask = SocialCollectionService.FetchAsync(SocialCollectionKind.List, CancellationToken.None);
                var antennasTask = SocialCollectionService.FetchAsync(SocialCollectionKind.Antenna, CancellationToken.None);
                var lists = await listsTask;
                var antennas = await antennasTask;
                if (version != feed.Version) return true;

                if (lists.Status != SocialApiStatus.Ok && antennas.Status != SocialApiStatus.Ok)
                {
                    var failure = lists.Status == SocialApiStatus.PermissionDenied || antennas.Status == SocialApiStatus.PermissionDenied
                                  ? FeedState.NeedsPermission
                                  : FeedState.Failed;
                    if (keepList && failure == FeedState.Failed) return false;

                    SetTimelineState(feed, failure);
                    return true;
                }

                _lists = lists.Status == SocialApiStatus.Ok ? lists.Value : NoCollections;
                _antennas = antennas.Status == SocialApiStatus.Ok ? antennas.Value : NoCollections;
                UpdateAntennaTabDot();
                ShowPickerForKind();
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: lists and antennas load failed", ex);
                if (version != feed.Version) return true;
                if (keepList) return false;

                SetTimelineState(feed, FeedState.Failed);
                return true;
            }
        }

        private void ResetCollections()
        {
            _lists = NoCollections;
            _antennas = NoCollections;
            _collections.SourceId = null;
            _antennaFlagsVersion++;
            LastListId = null;
            LastAntennaId = null;

            _changingCollection = true;
            try
            {
                CollectionPicker.ItemsSource = null;
            }
            finally
            {
                _changingCollection = false;
            }

            AntennasTabDot.Visibility = Visibility.Collapsed;
        }

        private void UpdateAntennaTabDot()
        {
            AntennasTabDot.Visibility = _antennas.Any(antenna => antenna.HasUnread) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MarkSelectedAntennaRead()
        {
            var selected = CollectionPicker.SelectedItem as SocialCollection;
            if (selected == null || selected.Kind != SocialCollectionKind.Antenna || !selected.HasUnread) return;
            if (!string.Equals(selected.Id, _collections.SourceId, StringComparison.Ordinal)) return;

            selected.HasUnread = false;
            UpdateAntennaTabDot();
        }

        private async void RefreshAntennaFlags()
        {
            try
            {
                if (_antennas.Count == 0) return;

                var version = ++_antennaFlagsVersion;
                var result = await SocialCollectionService.FetchAsync(SocialCollectionKind.Antenna, CancellationToken.None);
                if (version != _antennaFlagsVersion || result.Status != SocialApiStatus.Ok) return;

                var shown = _collectionKind == SocialCollectionKind.Antenna ? _collections.SourceId : null;
                foreach (var fresh in result.Value)
                {
                    var existing = _antennas.FirstOrDefault(antenna => string.Equals(antenna.Id, fresh.Id, StringComparison.Ordinal));
                    if (existing == null) continue;

                    existing.HasUnread = fresh.HasUnread && !string.Equals(existing.Id, shown, StringComparison.Ordinal);
                }

                UpdateAntennaTabDot();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not refresh the antennas' unread marks", ex);
            }
        }

        private void CollectionNewButton_Click(object sender, RoutedEventArgs e)
        {
            CreateCollection();
        }

        private async void CreateCollection()
        {
            try
            {
                var limit = SocialPolicies.Current.ListLimit;
                if (limit.HasValue && _lists != null && _lists.Count >= limit.Value)
                {
                    await DialogService.ShowMessageAsync(this,
                                                         LocalizedStrings.Get("SocialListCreateFailedTitle"),
                                                         LocalizedStrings.Format("SocialListTooManyFormat", limit.Value),
                                                         LocalizedStrings.Get("DialogOk"));
                    return;
                }

                var list = await SocialCollectionService.CreateListAsync(this);
                if (list == null || IsSignedOut) return;

                _lists = _lists.Concat(new[] { list }).ToList();
                LastListId = list.Id;
                LastCollectionKind = SocialCollectionKind.List;
                if (_collectionKind != SocialCollectionKind.List) ApplyCollectionKind(SocialCollectionKind.List);
                ShowPickerForKind();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not create the list", ex);
            }
        }

        private async void CollectionRename_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = CollectionPicker.SelectedItem as SocialCollection;
                if (selected == null) return;

                await SocialCollectionService.RenameListAsync(this, selected);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not rename the list", ex);
            }
        }

        private async void CollectionMembers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = CollectionPicker.SelectedItem as SocialCollection;
                if (selected == null) return;

                await SocialCollectionService.ShowMembersAsync(this, selected);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not show the list's members", ex);
            }
        }

        private async void CollectionDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = CollectionPicker.SelectedItem as SocialCollection;
                if (selected == null) return;

                if (!await SocialCollectionService.DeleteListAsync(this, selected)) return;

                _lists = _lists.Where(list => !string.Equals(list.Id, selected.Id, StringComparison.Ordinal)).ToList();
                LastListId = null;
                ShowPickerForKind();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not delete the list", ex);
            }
        }
    }
}
