using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ConnectivityKit.Tests
{
    public class ConnectivityWatcherTests
    {
        private FakeProbe _probe;
        private bool _hasInterface;
        private List<TimeSpan> _delays;
        private Func<CancellationToken, UniTask> _onDelay;
        private List<ConnectivityState> _changes;
        private ConnectivityWatcher _watcher;

        [SetUp]
        public void SetUp()
        {
            _probe = new FakeProbe();
            _hasInterface = true;
            _delays = new List<TimeSpan>();
            _onDelay = null;
            _changes = new List<ConnectivityState>();
            _watcher = new ConnectivityWatcher(_probe, new ConnectivitySettings { ProbeUrl = "http://test/healthcheck" },
                () => _hasInterface,
                (wait, ct) =>
                {
                    _delays.Add(wait);
                    return _onDelay != null ? _onDelay(ct) : UniTask.CompletedTask;
                });
            _watcher.OnChanged += s => _changes.Add(s);
        }

        [TearDown]
        public void TearDown()
        {
            _watcher.Stop();
        }

        /// <summary>Lets the loop run until it has waited count times, then stops it.</summary>
        private void StopAfterDelays(int count)
        {
            _onDelay = ct =>
            {
                if (_delays.Count >= count)
                {
                    _watcher.Stop();
                }
                return UniTask.CompletedTask;
            };
        }

        [Test]
        public void Initial_StateIsUnknown()
        {
            Assert.AreEqual(ConnectivityState.Unknown, _watcher.State);
            Assert.IsFalse(_watcher.IsRunning);
        }

        [UnityTest]
        public IEnumerator CheckNow_Reachable_GoesOnline_RaisesOnChangedOnce() => UniTask.ToCoroutine(async () =>
        {
            Assert.AreEqual(ConnectivityState.Online, await _watcher.CheckNowAsync());
            Assert.AreEqual(ConnectivityState.Online, await _watcher.CheckNowAsync());

            Assert.AreEqual(ConnectivityState.Online, _watcher.State);
            CollectionAssert.AreEqual(new[] { ConnectivityState.Online }, _changes);
        });

        [UnityTest]
        public IEnumerator CheckNow_NoNetworkInterface_OfflineWithoutProbing() => UniTask.ToCoroutine(async () =>
        {
            _hasInterface = false;

            Assert.AreEqual(ConnectivityState.Offline, await _watcher.CheckNowAsync());
            Assert.AreEqual(0, _probe.Calls);
        });

        [UnityTest]
        public IEnumerator CheckNow_ProbeThrows_IsOffline() => UniTask.ToCoroutine(async () =>
        {
            _probe.Throw = true;

            Assert.AreEqual(ConnectivityState.Offline, await _watcher.CheckNowAsync());
            Assert.AreEqual(ConnectivityState.Offline, _watcher.State);
        });

        [UnityTest]
        public IEnumerator CheckNow_Concurrent_SharesOneProbe() => UniTask.ToCoroutine(async () =>
        {
            _probe.Hold = true;
            UniTask<ConnectivityState> a = _watcher.CheckNowAsync();
            UniTask<ConnectivityState> b = _watcher.CheckNowAsync();
            UniTask<ConnectivityState> c = _watcher.CheckNowAsync();

            _probe.Release();
            (ConnectivityState ra, ConnectivityState rb, ConnectivityState rc) = await UniTask.WhenAll(a, b, c);

            Assert.AreEqual(1, _probe.Calls);
            CollectionAssert.AreEqual(new[] { ConnectivityState.Online, ConnectivityState.Online, ConnectivityState.Online }, new[] { ra, rb, rc });
        });

        [Test]
        public void Loop_Offline_BacksOff_2_4_8_16_30_30()
        {
            _probe.Default = ReachabilityVerdict.Unreachable;
            StopAfterDelays(6);

            _watcher.Start();

            CollectionAssert.AreEqual(new[] { 2, 4, 8, 16, 30, 30 }, _delays.Select(d => (int)d.TotalSeconds).ToArray());
            Assert.AreEqual(ConnectivityState.Offline, _watcher.State);
        }

        [Test]
        public void Loop_BackToOnline_ResetsBackoffAndUses30sInterval()
        {
            _probe.Results.Enqueue(ReachabilityVerdict.Unreachable);
            _probe.Results.Enqueue(ReachabilityVerdict.Unreachable);
            _probe.Results.Enqueue(ReachabilityVerdict.Reachable);
            _probe.Results.Enqueue(ReachabilityVerdict.Unreachable);
            StopAfterDelays(4);

            _watcher.Start();

            CollectionAssert.AreEqual(new[] { 2, 4, 30, 2 }, _delays.Select(d => (int)d.TotalSeconds).ToArray());
            CollectionAssert.AreEqual(new[] { ConnectivityState.Offline, ConnectivityState.Online, ConnectivityState.Offline }, _changes);
        }

        [UnityTest]
        public IEnumerator WaitForOnline_AlreadyOnline_ReturnsTrueImmediately() => UniTask.ToCoroutine(async () =>
        {
            await _watcher.CheckNowAsync();
            int calls = _probe.Calls;

            Assert.IsTrue(await _watcher.WaitForOnlineAsync());
            Assert.AreEqual(calls, _probe.Calls);
        });

        [UnityTest]
        public IEnumerator WaitForOnline_CompletesWhenStateFlipsOnline() => UniTask.ToCoroutine(async () =>
        {
            _probe.Default = ReachabilityVerdict.Unreachable;
            UniTask<bool> wait = _watcher.WaitForOnlineAsync();
            Assert.AreEqual(1, _probe.Calls, "checks once before waiting when the loop is not running");
            Assert.AreEqual(ConnectivityState.Offline, _watcher.State);

            _probe.Default = ReachabilityVerdict.Reachable;
            await _watcher.CheckNowAsync();

            Assert.IsTrue(await wait);
        });

        [UnityTest]
        public IEnumerator WaitForOnline_Cancelled_ReturnsFalse() => UniTask.ToCoroutine(async () =>
        {
            _probe.Default = ReachabilityVerdict.Unreachable;
            var cts = new CancellationTokenSource();
            UniTask<bool> wait = _watcher.WaitForOnlineAsync(cts.Token);

            cts.Cancel();

            Assert.IsFalse(await wait);
            cts.Dispose();
        });

        [UnityTest]
        public IEnumerator Stop_HaltsLoop_KeepsState() => UniTask.ToCoroutine(async () =>
        {
            var hold = new UniTaskCompletionSource();
            _onDelay = ct => hold.Task.AttachExternalCancellation(ct);
            _watcher.Start();
            Assert.IsTrue(_watcher.IsRunning);
            Assert.AreEqual(ConnectivityState.Online, _watcher.State);

            _watcher.Stop();
            hold.TrySetResult();
            await UniTask.Yield();

            Assert.IsFalse(_watcher.IsRunning);
            Assert.AreEqual(ConnectivityState.Online, _watcher.State);
            Assert.AreEqual(1, _probe.Calls);
        });

        [UnityTest]
        public IEnumerator CheckNow_ResetsLoopBackoff() => UniTask.ToCoroutine(async () =>
        {
            _probe.Default = ReachabilityVerdict.Unreachable;
            StopAfterDelays(3);
            _watcher.Start();
            await _watcher.CheckNowAsync();
            StopAfterDelays(4);
            _watcher.Start();

            CollectionAssert.AreEqual(new[] { 2, 4, 8, 2 }, _delays.Select(d => (int)d.TotalSeconds).ToArray());
        });
    }
}
