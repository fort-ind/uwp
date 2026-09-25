using System;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace Fort.ind_UWP
{
    public sealed class ViewLifetimeControl
    {
        private readonly object _lock = new object();

        private readonly ApplicationView _view;

        private int _refCount = 0;

        private bool _released = false;

        private EventHandler _releasedHandlers;

        private ViewLifetimeControl(CoreWindow window, string navTag, string title, string header, Type pageType)
        {
            Dispatcher = window.Dispatcher;
            Id = ApplicationView.GetApplicationViewIdForWindow(window);
            NavTag = navTag;
            Title = title;
            Header = header;
            PageType = pageType;

            _view = ApplicationView.GetForCurrentView();
            _view.Consolidated += OnConsolidated;
        }

        public static ViewLifetimeControl CreateForCurrentView(string navTag, string title, string header, Type pageType)
        {
            return new ViewLifetimeControl(CoreWindow.GetForCurrentThread(), navTag, title, header, pageType);
        }

        public CoreDispatcher Dispatcher { get; private set; }

        public int Id { get; private set; }

        public string NavTag { get; private set; }

        public string Title { get; private set; }

        public string Header { get; private set; }

        public Type PageType { get; private set; }

        public event EventHandler Released
        {
            add
            {
                lock (_lock)
                {
                    if (_released) throw new InvalidOperationException("This view is being disposed.");
                    _releasedHandlers += value;
                }
            }
            remove
            {
                lock (_lock)
                {
                    _releasedHandlers -= value;
                }
            }
        }

        public int StartViewInUse()
        {
            lock (_lock)
            {
                if (_released) throw new InvalidOperationException("This view is being disposed.");
                return ++_refCount;
            }
        }

        public int StopViewInUse()
        {
            lock (_lock)
            {
                if (_released) throw new InvalidOperationException("This view is being disposed.");

                var refCount = --_refCount;
                if (refCount == 0)
                {
                    var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, FinalizeRelease);
                }
                return refCount;
            }
        }

        private void OnConsolidated(ApplicationView sender, ApplicationViewConsolidatedEventArgs args)
        {
            try
            {
                StopViewInUse();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ViewLifetimeControl: view {Id} consolidated after release - {ex.Message}");
            }
        }

        private void FinalizeRelease()
        {
            EventHandler handlers;
            lock (_lock)
            {
                if (_refCount != 0 || _released) return;
                _released = true;
                handlers = _releasedHandlers;
                _releasedHandlers = null;
            }

            try
            {
                _view.Consolidated -= OnConsolidated;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ViewLifetimeControl: could not detach from view {Id} - {ex.Message}");
            }

            if (handlers == null)
            {
                Debug.WriteLine($"ViewLifetimeControl: view {Id} was released with nothing listening; its window stays open");
                return;
            }

            try
            {
                handlers(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ViewLifetimeControl: a Released handler for view {Id} threw - {ex.Message}");
            }
        }
    }
}
