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
    public interface ISocialFeedEntry
    {
        string PagingId { get; }
    }

    public enum SocialFeedPaging
    {
        FullPage,
        NonEmpty
    }

    public sealed class SocialFeedCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
        where T : class, ISocialFeedEntry
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<T>>> _loadPageAfter;

        private readonly SocialFeedPaging _paging;

        private bool _loading;

        private int _generation;

        private int _firstPageCount;

        public SocialFeedCollection(Func<string, CancellationToken, Task<IReadOnlyList<T>>> loadPageAfter, SocialFeedPaging paging)
        {
            _loadPageAfter = loadPageAfter;
            _paging = paging;
        }

        public bool HasMoreItems { get; private set; }

        public void ReplaceAll(IReadOnlyList<T> items, bool hasMore)
        {
            _generation++;
            _loading = false;

            ClearItems();
            foreach (var item in items)
            {
                Add(item);
            }
            HasMoreItems = hasMore;
            _firstPageCount = items.Count;
        }

        public int RemoveWhere(Func<T, bool> predicate)
        {
            var removed = 0;
            for (var i = Count - 1; i >= 0; i--)
            {
                if (!predicate(this[i])) continue;

                RemoveAt(i);
                if (i < _firstPageCount) _firstPageCount--;
                removed++;
            }

            return removed;
        }

        public bool TrimToFirstPage()
        {
            if (Count <= _firstPageCount) return false;

            var kept = new List<T>(_firstPageCount);
            for (int i = 0; i < _firstPageCount; i++)
            {
                kept.Add(this[i]);
            }

            ReplaceAll(kept, true);
            return true;
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
                var page = await _loadPageAfter(this[Count - 1].PagingId, cancellationToken);
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

                HasMoreItems = _paging == SocialFeedPaging.NonEmpty || page.Count >= AppConstants.SocialFeedPageSize;
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
