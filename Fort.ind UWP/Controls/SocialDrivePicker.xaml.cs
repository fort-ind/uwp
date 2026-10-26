using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialDrivePicker : UserControl
    {
        private const int FilePageSize = 30;

        private const int FolderLimit = 100;

        private static readonly object s_lastPathLock = new object();

        private static readonly Dictionary<string, IReadOnlyList<SocialDriveFolder>> s_lastPath =
            new Dictionary<string, IReadOnlyList<SocialDriveFolder>>(StringComparer.Ordinal);

        private readonly ObservableCollection<SocialDriveFolder> _folders = new ObservableCollection<SocialDriveFolder>();

        private readonly ObservableCollection<SocialDrivePickerItem> _files = new ObservableCollection<SocialDrivePickerItem>();

        private readonly List<SocialDriveFile> _selected = new List<SocialDriveFile>();

        private readonly HashSet<string> _attachedIds;

        private readonly int _limit;

        private readonly string _account;

        private List<SocialDriveFolder> _path = new List<SocialDriveFolder>();

        private CancellationTokenSource _loadCancellation;

        private string _untilId;

        private bool _syncing;

        private bool _started;

        public SocialDrivePicker(IEnumerable<string> attachedIds, int limit)
        {
            this.InitializeComponent();

            _attachedIds = new HashSet<string>(attachedIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _limit = Math.Max(0, limit);
            _account = SocialContentService.CurrentAccountId();

            FolderList.ItemsSource = _folders;
            FileGrid.ItemsSource = _files;

            var remembered = RememberedPath(_account);
            if (remembered != null) _path = new List<SocialDriveFolder>(remembered);
        }

        public event EventHandler SelectionCountChanged;

        public event EventHandler PermissionDenied;

        public bool WasDenied { get; private set; }

        public IReadOnlyList<SocialDriveFile> SelectedFiles
        {
            get { return _selected.ToList(); }
        }

        public int SelectedCount
        {
            get { return _selected.Count; }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;

            _started = true;
            Open(_path);
        }

        private static IReadOnlyList<SocialDriveFolder> RememberedPath(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;

            lock (s_lastPathLock)
            {
                IReadOnlyList<SocialDriveFolder> path;
                return s_lastPath.TryGetValue(account, out path) ? path : null;
            }
        }

        private void RememberPath()
        {
            if (string.IsNullOrEmpty(_account)) return;

            var snapshot = _path.ToArray();
            lock (s_lastPathLock)
            {
                s_lastPath[_account] = snapshot;
            }
        }

        private void Open(List<SocialDriveFolder> path)
        {
            _path = path;
            RememberPath();

            BackButton.IsEnabled = _path.Count > 0;
            var names = new List<string> { LocalizedStrings.Get("SocialDriveRoot") };
            names.AddRange(_path.Select(folder => folder.Name));
            PathText.Text = string.Join(LocalizedStrings.Get("SocialDrivePathSeparator"), names);

            _syncing = true;
            try
            {
                _folders.Clear();
                _files.Clear();
            }
            finally
            {
                _syncing = false;
            }

            FolderScroller.Visibility = Visibility.Collapsed;
            LoadMoreButton.Visibility = Visibility.Collapsed;
            _untilId = null;
            Load(false);
        }

        private string CurrentFolderId
        {
            get { return _path.Count == 0 ? null : _path[_path.Count - 1].Id; }
        }

        private async void Load(bool more)
        {
            var cancellation = RestartLoad();
            var folderId = CurrentFolderId;

            try
            {
                StatusPanel.Visibility = Visibility.Collapsed;
                LoadMoreButton.IsEnabled = false;
                if (!more) LoadingRing.IsActive = true;

                var token = await MisskeyAuthService.TryGetTokenAsync();

                Task<SocialApiResult<IReadOnlyList<SocialDriveFolder>>> foldersTask = null;
                if (!more) foldersTask = SocialApiService.GetDriveFoldersAsync(token, folderId, FolderLimit, cancellation.Token);
                var files = await SocialApiService.GetDriveFilesAsync(token, folderId, _untilId, FilePageSize, cancellation.Token);
                var folders = foldersTask == null ? null : await foldersTask;

                if (cancellation.IsCancellationRequested) return;

                var failed = files.Status != SocialApiStatus.Ok ? files.Status
                             : folders != null && folders.Status != SocialApiStatus.Ok ? folders.Status
                             : SocialApiStatus.Ok;
                if (failed == SocialApiStatus.PermissionDenied)
                {
                    WasDenied = true;
                    PermissionDenied?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (failed != SocialApiStatus.Ok)
                {
                    ShowStatus(LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(failed) ?? "SocialDriveLoadFailed"), true);
                    LoadMoreButton.IsEnabled = true;
                    return;
                }

                if (folders != null)
                {
                    foreach (var folder in folders.Value)
                    {
                        _folders.Add(folder);
                    }
                    FolderScroller.Visibility = _folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                AddFiles(files.Value);
                _untilId = files.Value.Count > 0 ? files.Value[files.Value.Count - 1].Id : _untilId;
                LoadMoreButton.Visibility = files.Value.Count >= FilePageSize ? Visibility.Visible : Visibility.Collapsed;
                LoadMoreButton.IsEnabled = true;

                if (_folders.Count == 0 && _files.Count == 0) ShowStatus(LocalizedStrings.Get("SocialDriveEmpty"), false);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SocialDrivePicker: load cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrivePicker: could not load the drive", ex);
                if (!cancellation.IsCancellationRequested) ShowStatus(LocalizedStrings.Get("SocialDriveLoadFailed"), true);
            }
            finally
            {
                if (!cancellation.IsCancellationRequested) LoadingRing.IsActive = false;
            }
        }

        private void AddFiles(IReadOnlyList<SocialDriveFile> files)
        {
            _syncing = true;
            try
            {
                foreach (var file in files)
                {
                    var item = new SocialDrivePickerItem(file, _attachedIds.Contains(file.Id));
                    _files.Add(item);
                    if (!item.IsAttached && _selected.Any(chosen => chosen.Id == file.Id)) FileGrid.SelectedItems.Add(item);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void ShowStatus(string message, bool retry)
        {
            StatusText.Text = message;
            RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            StatusPanel.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationHelper.AnnounceStatus(this, message, "SocialDriveStatus");
        }

        private CancellationTokenSource RestartLoad()
        {
            CancelLoad();
            _loadCancellation = new CancellationTokenSource();
            return _loadCancellation;
        }

        private void CancelLoad()
        {
            var cancellation = _loadCancellation;
            _loadCancellation = null;
            if (cancellation == null) return;

            try
            {
                cancellation.Cancel();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrivePicker: could not cancel the load", ex);
            }
        }

        private void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;

            _syncing = true;
            try
            {
                foreach (var item in e.RemovedItems.OfType<SocialDrivePickerItem>())
                {
                    _selected.RemoveAll(file => file.Id == item.File.Id);
                }

                foreach (var item in e.AddedItems.OfType<SocialDrivePickerItem>())
                {
                    if (item.IsAttached)
                    {
                        FileGrid.SelectedItems.Remove(item);
                        continue;
                    }

                    if (_selected.Any(file => file.Id == item.File.Id)) continue;

                    if (_selected.Count >= _limit)
                    {
                        FileGrid.SelectedItems.Remove(item);
                        AutomationHelper.AnnounceStatus(this,
                                                        LocalizedStrings.Format("SocialAttachmentLimitFormat", AppConstants.SocialAttachmentLimit),
                                                        "SocialAttachmentLimit");
                        continue;
                    }

                    _selected.Add(item.File);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrivePicker: selection failed", ex);
            }
            finally
            {
                _syncing = false;
            }

            SelectionCountChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FileGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            var item = args.Item as SocialDrivePickerItem;
            if (item != null && args.ItemContainer != null) args.ItemContainer.IsEnabled = !item.IsAttached;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var folder = (sender as FrameworkElement)?.DataContext as SocialDriveFolder;
            if (folder == null) return;

            var path = new List<SocialDriveFolder>(_path) { folder };
            Open(path);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_path.Count == 0) return;

            Open(_path.Take(_path.Count - 1).ToList());
        }

        private void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            Load(true);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0 && _folders.Count == 0) Open(_path);
            else Load(true);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CancelLoad();
        }
    }
}
