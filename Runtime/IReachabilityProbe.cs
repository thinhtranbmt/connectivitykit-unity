using System.Threading;
using Cysharp.Threading.Tasks;

namespace ConnectivityKit
{
    /// <summary>Checks once whether the server can be reached. Implementations should not throw.</summary>
    public interface IReachabilityProbe
    {
        UniTask<ReachabilityVerdict> ProbeAsync(CancellationToken ct);
    }
}
