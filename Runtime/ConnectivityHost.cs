using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ConnectivityKit
{
    /// <summary>
    /// Optional bridge from the Unity lifecycle: probes again when the app returns to the
    /// foreground and stops the watcher's loop when destroyed. Call <see cref="Init"/> once after
    /// creating the watcher; there is no static instance.
    /// </summary>
    public sealed class ConnectivityHost : MonoBehaviour
    {
        private ConnectivityWatcher _watcher;

        public void Init(ConnectivityWatcher watcher)
        {
            _watcher = watcher;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                _watcher?.CheckNowAsync().Forget();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                _watcher?.CheckNowAsync().Forget();
            }
        }

        private void OnDestroy()
        {
            _watcher?.Stop();
        }
    }
}
