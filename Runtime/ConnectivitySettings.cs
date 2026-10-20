using System;

namespace ConnectivityKit
{
    /// <summary>Where and how often to probe.</summary>
    public sealed class ConnectivitySettings
    {
        /// <summary>URL that answers HTTP 200 when the server is up, such as Nakama's /healthcheck.</summary>
        public string ProbeUrl { get; set; }

        /// <summary>How long one probe may take before it counts as unreachable.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Wait between probes while online.</summary>
        public TimeSpan OnlineInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>First wait while offline; it doubles after every failed probe up to OfflineBackoffMax.</summary>
        public TimeSpan OfflineBackoffMin { get; set; } = TimeSpan.FromSeconds(2);

        public TimeSpan OfflineBackoffMax { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Probes Nakama's health endpoint, for example ForNakama("https", "api.example.com", 443).</summary>
        public static ConnectivitySettings ForNakama(string scheme, string host, int port)
        {
            return new ConnectivitySettings { ProbeUrl = $"{scheme}://{host}:{port}/healthcheck" };
        }
    }
}
