using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public sealed class Debouncer
    {
        private CancellationTokenSource _cts;

        public static Task<bool> WaitAsync(int milliseconds, CancellationToken token)
        {
            return WaitAsync(TimeSpan.FromMilliseconds(milliseconds), token);
        }

        public static Task<bool> WaitAsync(TimeSpan delay, CancellationToken token)
        {
            return Task.Delay(delay, token).ContinueWith(waited => !waited.IsCanceled, CancellationToken.None,
                                                         TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public CancellationToken Restart()
        {
            Cancel();

            var cts = new CancellationTokenSource();
            _cts = cts;
            return cts.Token;
        }

        public void Cancel()
        {
            var cts = _cts;
            _cts = null;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                AppLog.Error("Debouncer: token source was already disposed", ex);
            }
            cts.Dispose();
        }
    }
}
