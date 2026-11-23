namespace Host2VMRelay;

internal sealed class ClashHealthSync(Func<HttpClient> openClient, Func<RelayHealth> readHealth, Action<string> log)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    internal async Task RefreshAsync(HttpClient api, ClashRelayProtocol protocol, CancellationToken token)
    {
        using var group = await ClashControlClient.ReadAsync(api, "proxies/" + protocol.Group, token).ConfigureAwait(false);
        var health = readHealth();
        bool ready = protocol == ClashRelayProtocol.Tcp ? health.Tcp : health.Udp;
        string expected = ready ? ClashRelayProtocol.Node : "PASS";
        if (group.RootElement.GetProperty("now").GetString() == expected) return;

        // /delay immediately changes Mihomo's per-URL health, even on timeout.
        // Only retest a disagreement. Give a ready relay the script's full budget;
        // an already unavailable local channel should fall back promptly.
        int timeoutMs = ready ? 2000 : 500;
        string url = Uri.EscapeDataString(protocol.HealthUrl);
        using var response = await api.GetAsync($"proxies/{ClashRelayProtocol.Node}/delay?url={url}&timeout={timeoutMs}&expected=204", token).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable &&
            response.StatusCode != System.Net.HttpStatusCode.GatewayTimeout)
            response.EnsureSuccessStatusCode();
        // The next read verifies the actual selection, including after a failed retest.
    }

    public Task RunAsync(CancellationToken token) =>
        Task.WhenAll(ClashRelayProtocol.All.Select(protocol => RunProtocolAsync(protocol, token)));

    private async Task RunProtocolAsync(ClashRelayProtocol protocol, CancellationToken token)
    {
        bool failed = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var api = openClient();
                while (!token.IsCancellationRequested)
                {
                    await RefreshAsync(api, protocol, token).ConfigureAwait(false);
                    if (failed) { log($"Clash {protocol.Label} 动态健康同步已恢复。"); failed = false; }
                    await Task.Delay(Interval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (!failed) log($"Clash {protocol.Label} 动态健康同步不可用：{ex.Message}。请检查 Clash 是否运行并加载扩展脚本。");
                failed = true;
            }
            try { await Task.Delay(Interval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
}
