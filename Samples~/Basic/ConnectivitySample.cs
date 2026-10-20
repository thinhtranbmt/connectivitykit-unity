using ConnectivityKit;
using UnityEngine;

/// <summary>
/// Probes a local Nakama (nakama-core-go stack) and logs every state change. Drop on a GameObject
/// and press Play; stop the server to see Offline and the backoff.
/// </summary>
public sealed class ConnectivitySample : MonoBehaviour
{
    [SerializeField] private string _scheme = "http";
    [SerializeField] private string _host = "127.0.0.1";
    [SerializeField] private int _port = 7350;

    private ConnectivityWatcher _watcher;

    private void Start()
    {
        ConnectivitySettings settings = ConnectivitySettings.ForNakama(_scheme, _host, _port);
        _watcher = new ConnectivityWatcher(new HttpReachabilityProbe(settings), settings);
        _watcher.OnChanged += state => Debug.Log($"[ConnectivitySample] {state}");
        gameObject.AddComponent<ConnectivityHost>().Init(_watcher);
        _watcher.Start();
    }
}
