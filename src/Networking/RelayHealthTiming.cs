namespace Host2VMRelay;

/// <summary>Allow VM scheduling delays without expiring health during an in-flight probe.</summary>
internal static class RelayHealthTiming
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    // The Rust worker has the same five-second remote budget. Allow pipe and
    // scheduler delivery to finish before the parent cancels its response.
    internal static readonly TimeSpan ControlProbeTimeout = ProbeTimeout + TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan Lease = ControlProbeTimeout + PollInterval + TimeSpan.FromSeconds(1);
}
