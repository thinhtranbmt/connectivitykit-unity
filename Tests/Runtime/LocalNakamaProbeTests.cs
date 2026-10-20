using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ConnectivityKit.IntegrationTests
{
    /// <summary>
    /// Runs against the local stack of nakama-core-go (docker compose up -d in MyCore/nakama-core-go):
    /// Nakama 3.41.0 answers GET/HEAD /healthcheck with 200 at http://127.0.0.1:7350.
    /// </summary>
    [Category("Integration")]
    public class LocalNakamaProbeTests
    {
        [UnityTest]
        public IEnumerator LocalNakama_Healthcheck_IsReachable() => UniTask.ToCoroutine(async () =>
        {
            var probe = new HttpReachabilityProbe(ConnectivitySettings.ForNakama("http", "127.0.0.1", 7350));
            var watcher = new ConnectivityWatcher(probe, ConnectivitySettings.ForNakama("http", "127.0.0.1", 7350), () => true);

            Assert.AreEqual(ReachabilityVerdict.Reachable, await probe.ProbeAsync(CancellationToken.None));
            Assert.IsTrue(await watcher.WaitForOnlineAsync());
        });

        [UnityTest]
        public IEnumerator NoServer_IsUnreachableWithinTimeout() => UniTask.ToCoroutine(async () =>
        {
            var probe = new HttpReachabilityProbe(ConnectivitySettings.ForNakama("http", "127.0.0.1", 7399));
            Stopwatch watch = Stopwatch.StartNew();

            Assert.AreEqual(ReachabilityVerdict.Unreachable, await probe.ProbeAsync(CancellationToken.None));
            Assert.LessOrEqual(watch.Elapsed, TimeSpan.FromSeconds(6));
        });
    }
}
