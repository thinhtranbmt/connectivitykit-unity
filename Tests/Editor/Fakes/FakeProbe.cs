using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ConnectivityKit.Tests
{
    /// <summary>
    /// Answers from Results (then Default). With Hold set, every probe waits until Release.
    /// </summary>
    public sealed class FakeProbe : IReachabilityProbe
    {
        public readonly Queue<ReachabilityVerdict> Results = new Queue<ReachabilityVerdict>();
        public ReachabilityVerdict Default = ReachabilityVerdict.Reachable;
        public bool Throw;
        public bool Hold;
        public int Calls;

        private UniTaskCompletionSource _gate = new UniTaskCompletionSource();

        public void Release()
        {
            UniTaskCompletionSource gate = _gate;
            _gate = new UniTaskCompletionSource();
            gate.TrySetResult();
        }

        public async UniTask<ReachabilityVerdict> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            if (Hold)
            {
                await _gate.Task;
            }
            if (Throw)
            {
                throw new InvalidOperationException("probe failed");
            }
            return Results.Count > 0 ? Results.Dequeue() : Default;
        }
    }
}
