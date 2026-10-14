namespace MasterServer.Telemetry;

/// <summary>A machine is online when last-seen is within the stale window (default 72 hours).</summary>
public static class MachinePresence
{
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(72);

    public static DateTime EffectiveLastSeen(DateTime? lastSeenAt, DateTime activatedAt) =>
        lastSeenAt ?? activatedAt;

    public static bool IsOnline(DateTime? lastSeenAt, DateTime activatedAt, DateTime now, TimeSpan staleAfter) =>
        now - EffectiveLastSeen(lastSeenAt, activatedAt) <= staleAfter;

    public static TimeSpan StaleAfterFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("HEARTBEAT_STALE_HOURS");
        if (int.TryParse(raw, out var hours) && hours > 0)
            return TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 30));
        return DefaultStaleAfter;
    }
}
