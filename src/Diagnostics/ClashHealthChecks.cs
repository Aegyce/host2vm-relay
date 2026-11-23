using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

namespace Host2VMRelay;

internal static class ClashHealthChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        internal string Selected = ClashRelayProtocol.Node;
        internal HttpStatusCode ProbeStatus = HttpStatusCode.OK;
        internal HttpStatusCode GroupStatus = HttpStatusCode.OK;
        internal readonly List<string> Probes = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath.EndsWith("/delay", StringComparison.Ordinal))
            {
                Probes.Add(request.RequestUri.PathAndQuery);
                return Task.FromResult(new HttpResponseMessage(ProbeStatus));
            }
            return Task.FromResult(new HttpResponseMessage(GroupStatus)
                { Content = JsonContent.Create(new { now = Selected }) });
        }
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var handler = new Handler();
        using var api = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var health = new RelayHealth(true, false);
        var sync = new ClashHealthSync(() => api, () => health, _ => { });
        Task Tick(ClashRelayProtocol protocol) => sync.RefreshAsync(api, protocol, CancellationToken.None);

        for (int i = 0; i < 3; i++) await Tick(ClashRelayProtocol.Tcp);
        check(handler.Probes.Count == 0, "healthy TCP routing never submits a mutating URL test on steady-state ticks");
        await Tick(ClashRelayProtocol.Udp);
        check(handler.Probes.Count == 1 && handler.Probes[0].Contains("%2Fudp&timeout=500&expected=204"),
            "confirmed local UDP loss only retests UDP with the fallback budget");
        handler.Selected = "PASS";
        await Tick(ClashRelayProtocol.Udp);
        check(handler.Probes.Count == 1, "confirmed fallback does not trigger repeated probes");
        foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout, HttpStatusCode.OK })
        {
            handler.ProbeStatus = status;
            await Tick(ClashRelayProtocol.Tcp);
        }
        check(handler.Probes.Count == 4 && handler.Probes.Skip(1).All(p => p.Contains("%2Ftcp&timeout=2000&expected=204")),
            "TCP selection disagreement is rechecked until recovery with the full healthy probe budget");
        handler.Selected = ClashRelayProtocol.Node;
        await Tick(ClashRelayProtocol.Tcp);
        check(handler.Probes.Count == 4, "confirmed TCP recovery stops active retests");
        handler.GroupStatus = HttpStatusCode.NotFound;
        bool rejected = false;
        try { await Tick(ClashRelayProtocol.Tcp); } catch (HttpRequestException) { rejected = true; }
        check(rejected && handler.Probes.Count == 4, "unreadable selection never triggers a mutating probe");

        var logs = new ConcurrentQueue<string>();
        using var cancel = new CancellationTokenSource();
        int attempts = 0;
        sync = new ClashHealthSync(() => { Interlocked.Increment(ref attempts); throw new IOException("controller unavailable"); },
            () => health, logs.Enqueue);
        Task running = sync.RunAsync(cancel.Token);
        await Task.Delay(ClashHealthSync.Interval * 2 + TimeSpan.FromMilliseconds(50));
        cancel.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(1));
        check(attempts >= 4 && logs.Count == 2, "per-protocol controller failures retry with deduplicated logs and cancel promptly");
    }
}
