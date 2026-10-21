# ConnectivityKit

Knows whether your **server** is reachable, not just whether the phone has Wi-Fi. An HTTP probe hits a health URL; a watcher keeps an `Online` / `Offline` state, probes again on a backoff while offline, and lets an offline queue wait for the network with `WaitForOnlineAsync`.

Plain classes, no singleton, seams behind interfaces — the same convention as `NakamaKit`, `HttpKit` and `IAPKit`.

## Requirements

| Dependency | How it resolves |
| --- | --- |
| Unity 2022.3+ | — |
| UniTask `com.cysharp.unitask` 2.5.10 | OpenUPM — add the scoped registry below |

## Install

1. Add the OpenUPM scoped registry once per project in `Packages/manifest.json`:
   ```json
   "scopedRegistries": [
     { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": [ "com.cysharp" ] }
   ]
   ```
2. Add the package:
   ```json
   "com.mycore.connectivitykit": "https://github.com/thinhtranbmt/connectivitykit-unity.git#v0.1.0"
   ```

## Pieces

| Type | What it does |
| --- | --- |
| `ConnectivitySettings` | `ProbeUrl`, `Timeout` (5 s), `OnlineInterval` (30 s), `OfflineBackoffMin`/`Max` (2 s / 30 s). `ForNakama(scheme, host, port)` probes `/healthcheck`. |
| `IReachabilityProbe`, `HttpReachabilityProbe` | One probe: `HEAD` (then `GET` if the server answers 405 or 501 — Nakama answers `HEAD` with 501), no redirects, `Reachable` only for HTTP 200. Never throws. |
| `ConnectivityWatcher` | `State` (`Unknown` / `Online` / `Offline`), `OnChanged` (only on a change), `CheckNowAsync`, `WaitForOnlineAsync`, `Start` / `Stop` for the probe loop. Never throws. |
| `ConnectivityHost` | Optional MonoBehaviour: probes again when the app returns to the foreground and stops the loop when destroyed. |

## Usage

```csharp
ConnectivitySettings settings = ConnectivitySettings.ForNakama("https", "api.example.com", 443);
var watcher = new ConnectivityWatcher(new HttpReachabilityProbe(settings), settings);
watcher.OnChanged += state => offlineBanner.SetActive(state == ConnectivityState.Offline);
gameObject.AddComponent<ConnectivityHost>().Init(watcher);
watcher.Start();
```

The loop probes every 30 s while online. While offline it waits 2 s, 4 s, 8 s, 16 s, then 30 s between probes; going back online, or calling `CheckNowAsync`, restarts that backoff. A device with no network interface at all is `Offline` at once, without a request. Several `CheckNowAsync` calls in flight share one probe.

### Offline queues

```csharp
// Sends queued answers once the server is reachable; leaving the screen cancels the wait.
while (queue.HasItems)
{
    if (!await watcher.WaitForOnlineAsync(destroyCancellationToken))
    {
        return; // cancelled: nothing thrown, the queue keeps its items
    }
    RpcResult<SubmitRes> result = await rpc.CallAsync<SubmitReq, SubmitRes>("exam.submit", queue.Peek());
    if (result.IsOk)
    {
        queue.Dequeue(); // only a confirmed submission leaves the queue
        continue;
    }
    if (result.Status == RpcStatus.Network || result.Status == RpcStatus.Timeout)
    {
        await watcher.CheckNowAsync(); // the server vanished again: Offline until a probe succeeds
        continue;
    }
    return; // signed out, server error, bad payload: keep the item, surface it, try again later
}
```

`WaitForOnlineAsync` returns `true` at once when online. When the loop is not running it probes once before waiting; after that it waits for a state change from the loop or another `CheckNowAsync`, so start the loop (or keep calling `CheckNowAsync`) while something waits.

## Why only a direct 200 counts

School and café Wi-Fi often sits behind a captive portal: every HTTP request is redirected to a login page that itself answers 200. Following redirects would read that as online and the app would try, and fail, to sync. The probe therefore sets `redirectLimit = 0` and only accepts a 200 from the probe URL itself; a redirect, an error status, a timeout or a connection error is `Unreachable`.

## Tests

EditMode tests use a fake probe and a local `HttpListener`. The integration tests (`[Category("Integration")]`, PlayMode) need the local stack of `nakama-core-go` (`docker compose up -d --build`) at `http://127.0.0.1:7350`.
