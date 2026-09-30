using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace Fort.ind_UWP
{
    public sealed class SocialFeedCollection : ObservableCollection<SocialFeedItem>, ISupportIncrementalLoading
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<SocialFeedItem>>> _loadPageAfter;

        private bool _loading;

        private int _generation;

        public SocialFeedCollection(Func<string, CancellationToken, Task<IReadOnlyList<SocialFeedItem>>> loadPageAfter)
        {
            _loadPageAfter = loadPageAfter;
        }

        public bool HasMoreItems { get; private set; }

        public void ReplaceAll(IReadOnlyList<SocialFeedItem> items, bool hasMore)
        {
            _generation++;
            _loading = false;

            ClearItems();
            foreach (var item in items)
            {
                Add(item);
            }
            HasMoreItems = hasMore;
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run(LoadMoreCoreAsync);
        }

        private async Task<LoadMoreItemsResult> LoadMoreCoreAsync(CancellationToken cancellationToken)
        {
            if (_loading || !HasMoreItems || Count == 0) return new LoadMoreItemsResult { Count = 0 };

            var generation = _generation;
            _loading = true;
            try
            {
                var page = await _loadPageAfter(this[Count - 1].Id, cancellationToken);
                if (generation != _generation) return new LoadMoreItemsResult { Count = 0 };

                if (page == null || page.Count == 0)
                {
                    HasMoreItems = false;
                    return new LoadMoreItemsResult { Count = 0 };
                }

                foreach (var item in page)
                {
                    Add(item);
                }

                HasMoreItems = page.Count >= AppConstants.SocialFeedPageSize;
                return new LoadMoreItemsResult { Count = (uint)page.Count };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("SocialFeedCollection: the list cancelled a load");
                return new LoadMoreItemsResult { Count = 0 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialFeedCollection: could not load more - {ex.Message}");
                if (generation == _generation) HasMoreItems = false;
                return new LoadMoreItemsResult { Count = 0 };
            }
            finally
            {
                if (generation == _generation) _loading = false;
            }
        }
    }
}
