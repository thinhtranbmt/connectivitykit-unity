using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ConnectivityKit
{
    /// <summary>
    /// Tracks whether the server is reachable. Start() probes in a loop: every OnlineInterval while
    /// online, and after 2 s, 4 s, 8 s… capped at 30 s while offline. CheckNowAsync probes at once
    /// (callers that overlap share one probe) and restarts the offline backoff. A device without any
    /// network interface is Offline without probing. Nothing here throws.
    /// </summary>
    public sealed class ConnectivityWatcher
    {
        private readonly IReachabilityProbe _probe;
        private readonly ConnectivitySettings _settings;
        private readonly Func<bool> _hasNetworkInterface;
        private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;

        private UniTaskCompletionSource<ConnectivityState> _pending;
        private CancellationTokenSource _loopCts;
        private TimeSpan _backoff;

        public ConnectivityWatcher(IReachabilityProbe probe, ConnectivitySettings settings,
            Func<bool> hasNetworkInterface = null, Func<TimeSpan, CancellationToken, UniTask> delay = null)
        {
            _probe = probe;
            _settings = settings;
            _hasNetworkInterface = hasNetworkInterface
                ?? (() => Application.internetReachability != NetworkReachability.NotReachable);
            // Real time: a paused game (Time.timeScale = 0) must keep probing.
            _delay = delay ?? ((wait, ct) => UniTask.Delay(wait, DelayType.Realtime, cancellationToken: ct));
            _backoff = settings.OfflineBackoffMin;
        }

        /// <summary>Raised when State changes, never twice with the same value.</summary>
        public event Action<ConnectivityState> OnChanged;

        public ConnectivityState State { get; private set; } = ConnectivityState.Unknown;

        public bool IsRunning => _loopCts != null;

        /// <summary>Probes now and returns the new state; a cancelled wait returns the current state.</summary>
        public async UniTask<ConnectivityState> CheckNowAsync(CancellationToken ct = default)
        {
            _backoff = _settings.OfflineBackoffMin;
            (bool cancelled, ConnectivityState state) = await ProbeSharedAsync()
                .AttachExternalCancellation(ct).SuppressCancellationThrow();
            return cancelled ? State : state;
        }

        /// <summary>
        /// True once the server is reachable; false when ct is cancelled. Probes once first when the
        /// loop is not running, then waits for a state change from the loop or another CheckNowAsync.
        /// </summary>
        public async UniTask<bool> WaitForOnlineAsync(CancellationToken ct = default)
        {
            if (State == ConnectivityState.Online)
            {
                return true;
            }
            if (!IsRunning && await CheckNowAsync(ct) == ConnectivityState.Online)
            {
                return true;
            }
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            var online = new UniTaskCompletionSource<bool>();
            void Handler(ConnectivityState state)
            {
                if (state == ConnectivityState.Online)
                {
                    online.TrySetResult(true);
                }
            }
            OnChanged += Handler;
            try
            {
                if (State == ConnectivityState.Online)
                {
                    return true;
                }
                (bool cancelled, bool result) = await online.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
                return !cancelled && result;
            }
            finally
            {
                OnChanged -= Handler;
            }
        }

        /// <summary>Starts the probe loop. Calling it while running does nothing.</summary>
        public void Start()
        {
            if (_loopCts != null)
            {
                return;
            }
            _loopCts = new CancellationTokenSource();
            LoopAsync(_loopCts.Token).Forget();
        }

        /// <summary>Stops the probe loop; State keeps its last value.</summary>
        public void Stop()
        {
            if (_loopCts == null)
            {
                return;
            }
            _loopCts.Cancel();
            _loopCts.Dispose();
            _loopCts = null;
        }

        private async UniTaskVoid LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                ConnectivityState state = await ProbeSharedAsync();
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                TimeSpan wait;
                if (state == ConnectivityState.Online)
                {
                    _backoff = _settings.OfflineBackoffMin;
                    wait = _settings.OnlineInterval;
                }
                else
                {
                    wait = _backoff;
                    _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, _settings.OfflineBackoffMax.Ticks));
                }
                try
                {
                    await _delay(wait, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private UniTask<ConnectivityState> ProbeSharedAsync()
        {
            if (!_hasNetworkInterface())
            {
                SetState(ConnectivityState.Offline);
                return UniTask.FromResult(ConnectivityState.Offline);
            }
            UniTaskCompletionSource<ConnectivityState> pending = _pending;
            if (pending == null)
            {
                pending = _pending = new UniTaskCompletionSource<ConnectivityState>();
                RunProbeAsync(pending).Forget();
            }
            return pending.Task;
        }

        private async UniTaskVoid RunProbeAsync(UniTaskCompletionSource<ConnectivityState> pending)
        {
            ReachabilityVerdict verdict;
            try
            {
                // The probe enforces Settings.Timeout; no single caller's token may cancel a shared probe.
                verdict = await _probe.ProbeAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                verdict = ReachabilityVerdict.Unreachable;
            }
            ConnectivityState state = verdict == ReachabilityVerdict.Reachable ? ConnectivityState.Online : ConnectivityState.Offline;
            if (_pending == pending)
            {
                _pending = null;
            }
            SetState(state);
            pending.TrySetResult(state);
        }

        private void SetState(ConnectivityState state)
        {
            if (State == state)
            {
                return;
            }
            State = state;
            try
            {
                OnChanged?.Invoke(state);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
