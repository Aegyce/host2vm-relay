using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Host2VMRelay;

internal static class SlowHealthChecks
{
    public static async Task RunAsync(Action<bool, string> check, CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
        using var peer = await listener.AcceptTcpClientAsync(token);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stream = peer.GetStream();
        int delayMs = 0;
        var responder = Task.Run(async () =>
        {
            async Task Send(byte[] frame)
            {
                byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, frame.Length);
                await stream.WriteAsync(size, stop.Token);
                await stream.WriteAsync(frame, stop.Token);
            }
            await Send(new byte[] { (byte)'H', 0, 0, 0, 0 }
                .Concat(Encoding.ASCII.GetBytes("Host2VMRelay-UDP/1")).ToArray());
            try
            {
                while (true)
                {
                    byte[] size = new byte[4]; await stream.ReadExactlyAsync(size, stop.Token);
                    byte[] frame = new byte[BinaryPrimitives.ReadInt32BigEndian(size)];
                    await stream.ReadExactlyAsync(frame, stop.Token);
                    if (frame[0] != (byte)'P') continue;
                    await Task.Delay(Volatile.Read(ref delayMs), stop.Token);
                    frame[0] = (byte)'R'; await Send(frame);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }, token);
        try
        {
            using var tunnel = await UdpTunnel.OpenStreamsAsync(client.GetStream(), client.GetStream(), () => { }, token);
            using var relay = new RelaySocksServer(0);
            relay.SetUpstream(12345, tunnel);
            Volatile.Write(ref delayMs, 4000);
            Task<bool> probe = tunnel.ProbeAsync(token);
            await Task.Delay(1000, token);
            check(!probe.IsCompleted && relay.TcpHealthy && relay.UdpHealthy,
                "in-flight slow probe preserves TCP/UDP health beyond the former 600ms lease");
            check(await probe, "UDP probe accepts a four-second scheduling delay");
            relay.SetUpstream(12345, tunnel);
            Volatile.Write(ref delayMs, 6500);
            check(!await tunnel.ProbeAsync(token), "unresponsive UDP probe still times out after five seconds");
            relay.SetUpstream(0, null);
            check(!relay.TcpHealthy && !relay.UdpHealthy, "explicit disconnect overrides the longer health lease immediately");
        }
        finally { stop.Cancel(); await responder; }
    }
}
