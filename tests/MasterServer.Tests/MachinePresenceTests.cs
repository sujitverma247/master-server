using MasterServer.Telemetry;
using Xunit;

namespace MasterServer.Tests;

public class MachinePresenceTests
{
    [Fact]
    public void Last_seen_falls_back_to_activated()
    {
        var activated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(activated, MachinePresence.EffectiveLastSeen(null, activated));
        var seen = activated.AddHours(2);
        Assert.Equal(seen, MachinePresence.EffectiveLastSeen(seen, activated));
    }

    [Fact]
    public void Online_within_stale_window()
    {
        var activated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = activated.AddHours(24);
        Assert.True(MachinePresence.IsOnline(activated.AddHours(23), activated, now, TimeSpan.FromHours(72)));
        Assert.False(MachinePresence.IsOnline(activated.AddHours(-1), activated, now.AddDays(10), TimeSpan.FromHours(72)));
    }

    [Fact]
    public void Exactly_at_stale_boundary_is_online()
    {
        var activated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var stale = TimeSpan.FromHours(72);
        Assert.True(MachinePresence.IsOnline(activated, activated, activated.Add(stale), stale));
        Assert.False(MachinePresence.IsOnline(activated, activated, activated.Add(stale).AddSeconds(1), stale));
    }
}
