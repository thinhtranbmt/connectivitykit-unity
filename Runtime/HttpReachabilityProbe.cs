using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace ConnectivityKit
{
    /// <summary>
    /// Sends HEAD to Settings.ProbeUrl (GET when the server answers 405 or 501) without following
    /// redirects. Only an HTTP 200 straight from the probe URL is Reachable: a captive portal that
    /// redirects to its login page, an error status, a timeout, a connection error or cancellation
    /// are all Unreachable. Never throws.
    /// </summary>
    public sealed class HttpReachabilityProbe : IReachabilityProbe
    {
        private readonly ConnectivitySettings _settings;

        public HttpReachabilityProbe(ConnectivitySettings settings)
        {
            _settings = settings;
        }

        public async UniTask<ReachabilityVerdict> ProbeAsync(CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // CancelAfterSlim runs on the player loop, so the abort happens on the main thread.
                cts.CancelAfterSlim(_settings.Timeout, DelayType.Realtime);
                try
                {
                    long status = await SendAsync(UnityWebRequest.kHttpVerbHEAD, cts.Token);
                    // 405 Method Not Allowed, or 501 Not Implemented as Nakama answers HEAD.
                    if (status == 405 || status == 501)
                    {
                        status = await SendAsync(UnityWebRequest.kHttpVerbGET, cts.Token);
                    }
                    return status == 200 ? ReachabilityVerdict.Reachable : ReachabilityVerdict.Unreachable;
                }
                catch (Exception)
                {
                    return ReachabilityVerdict.Unreachable;
                }
            }
        }

        private async UniTask<long> SendAsync(string method, CancellationToken ct)
        {
            using (var request = new UnityWebRequest(_settings.ProbeUrl, method))
            {
                request.redirectLimit = 0;
                request.timeout = Math.Max(1, (int)Math.Ceiling(_settings.Timeout.TotalSeconds));
                request.downloadHandler = new DownloadHandlerBuffer();
                try
                {
                    await request.SendWebRequest().WithCancellation(ct);
                }
                catch (UnityWebRequestException)
                {
                    // HTTP errors and redirects land here; the status code below decides.
                }
                return request.responseCode;
            }
        }
    }
}
