using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ConnectivityKit.Tests
{
    public class HttpReachabilityProbeTests
    {
        /// <summary>An HttpListener on a free loopback port; Handler answers each request on a pool thread.</summary>
        private sealed class TestServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly List<HttpListenerContext> _held = new List<HttpListenerContext>();

            public TestServer()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                Task.Run(AcceptLoop);
            }

            public int Port { get; }

            public Action<HttpListenerContext> Handler = ctx => Reply(ctx, 200);

            public readonly List<string> Methods = new List<string>();

            public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

            public static void Reply(HttpListenerContext ctx, int status, string body = "{}")
            {
                ctx.Response.StatusCode = status;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                if (ctx.Request.HttpMethod != "HEAD")
                {
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                ctx.Response.Close();
            }

            public void Hold(HttpListenerContext ctx)
            {
                lock (_held)
                {
                    _held.Add(ctx);
                }
            }

            private async Task AcceptLoop()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    lock (Methods)
                    {
                        Methods.Add(ctx.Request.HttpMethod + " " + ctx.Request.Url.AbsolutePath);
                    }
                    Handler(ctx);
                }
            }

            public void Dispose()
            {
                _listener.Close();
            }
        }

        private TestServer _server;

        [SetUp]
        public void SetUp()
        {
            _server = new TestServer();
        }

        [TearDown]
        public void TearDown()
        {
            _server.Dispose();
        }

        private HttpReachabilityProbe Probe(string path = "/healthcheck", TimeSpan? timeout = null)
        {
            return new HttpReachabilityProbe(new ConnectivitySettings
            {
                ProbeUrl = _server.Url(path),
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            });
        }

        [UnityTest]
        public IEnumerator HttpProbe_200_IsReachable() => UniTask.ToCoroutine(async () =>
        {
            Assert.AreEqual(ReachabilityVerdict.Reachable, await Probe().ProbeAsync(CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "HEAD /healthcheck" }, _server.Methods);
        });

        [UnityTest]
        public IEnumerator HttpProbe_Redirect302_IsUnreachable() => UniTask.ToCoroutine(async () =>
        {
            // A captive portal: the probe URL redirects to a login page that answers 200.
            _server.Handler = ctx =>
            {
                if (ctx.Request.Url.AbsolutePath == "/healthcheck")
                {
                    ctx.Response.RedirectLocation = _server.Url("/portal");
                    TestServer.Reply(ctx, 302, "");
                }
                else
                {
                    TestServer.Reply(ctx, 200, "<html>Wi-Fi login</html>");
                }
            };

            Assert.AreEqual(ReachabilityVerdict.Unreachable, await Probe().ProbeAsync(CancellationToken.None));
            CollectionAssert.DoesNotContain(_server.Methods, "HEAD /portal");
            CollectionAssert.DoesNotContain(_server.Methods, "GET /portal");
        });

        [UnityTest]
        public IEnumerator HttpProbe_503_IsUnreachable() => UniTask.ToCoroutine(async () =>
        {
            _server.Handler = ctx => TestServer.Reply(ctx, 503);

            Assert.AreEqual(ReachabilityVerdict.Unreachable, await Probe().ProbeAsync(CancellationToken.None));
        });

        [UnityTest]
        public IEnumerator HttpProbe_HeadNotAllowed_FallsBackToGet() => UniTask.ToCoroutine(async () =>
        {
            _server.Handler = ctx => TestServer.Reply(ctx, ctx.Request.HttpMethod == "HEAD" ? 405 : 200);

            Assert.AreEqual(ReachabilityVerdict.Reachable, await Probe().ProbeAsync(CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "HEAD /healthcheck", "GET /healthcheck" }, _server.Methods);
        });

        [UnityTest]
        public IEnumerator HttpProbe_HeadNotImplemented_FallsBackToGet() => UniTask.ToCoroutine(async () =>
        {
            // Nakama 3.41.0 answers HEAD /healthcheck with 501.
            _server.Handler = ctx => TestServer.Reply(ctx, ctx.Request.HttpMethod == "HEAD" ? 501 : 200);

            Assert.AreEqual(ReachabilityVerdict.Reachable, await Probe().ProbeAsync(CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "HEAD /healthcheck", "GET /healthcheck" }, _server.Methods);
        });

        [UnityTest]
        public IEnumerator HttpProbe_Timeout_IsUnreachable() => UniTask.ToCoroutine(async () =>
        {
            _server.Handler = _server.Hold;
            DateTime start = DateTime.UtcNow;

            ReachabilityVerdict verdict = await Probe(timeout: TimeSpan.FromMilliseconds(200)).ProbeAsync(CancellationToken.None);

            Assert.AreEqual(ReachabilityVerdict.Unreachable, verdict);
            Assert.Less((DateTime.UtcNow - start).TotalSeconds, 2, "the 200 ms timeout was not honoured");
        });

        [UnityTest]
        public IEnumerator HttpProbe_Cancelled_IsUnreachableWithoutThrowing() => UniTask.ToCoroutine(async () =>
        {
            _server.Handler = _server.Hold;
            var cts = new CancellationTokenSource();
            UniTask<ReachabilityVerdict> probe = Probe().ProbeAsync(cts.Token);

            cts.Cancel();

            Assert.AreEqual(ReachabilityVerdict.Unreachable, await probe);
            cts.Dispose();
        });
    }
}
