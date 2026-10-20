# Changelog

## 0.1.0

- `HttpReachabilityProbe`: HEAD (GET after 405/501), no redirects, only a direct HTTP 200 is reachable, own timeout, never throws.
- `ConnectivityWatcher`: Online/Offline state with change events, shared concurrent probes, probe loop with a 30 s online interval and a 2–30 s offline backoff, `WaitForOnlineAsync` that returns false on cancellation.
- `ConnectivitySettings.ForNakama` for Nakama's `/healthcheck`.
- `ConnectivityHost` for foreground re-checks; `Basic` sample.
- Integration tests against a local Nakama 3.41.0.
