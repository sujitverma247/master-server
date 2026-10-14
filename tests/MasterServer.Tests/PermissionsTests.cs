using MasterServer.Admin;
using Xunit;

namespace MasterServer.Tests;

public class PermissionsTests
{
    [Fact]
    public void Admin_role_is_granted_every_permission_in_the_catalog()
    {
        var adminGrants = Permissions.DefaultRoleGrants[Permissions.RoleAdmin];
        Assert.Equal(Permissions.Catalog.Count, adminGrants.Length);
        Assert.All(Permissions.Catalog.Keys, code => Assert.Contains(code, adminGrants));
    }

    [Fact]
    public void Agent_role_is_read_only_and_cannot_manage_users_or_issue_keys()
    {
        var agentGrants = Permissions.DefaultRoleGrants[Permissions.RoleAgent];
        Assert.DoesNotContain(Permissions.UserManage, agentGrants);
        Assert.DoesNotContain(Permissions.KeyIssue, agentGrants);
        Assert.DoesNotContain(Permissions.KeyRevoke, agentGrants);
        Assert.DoesNotContain(Permissions.CustomerWrite, agentGrants);
        Assert.DoesNotContain(Permissions.PaymentWrite, agentGrants);
        Assert.DoesNotContain(Permissions.MailWrite, agentGrants);
        Assert.DoesNotContain(Permissions.InvoiceWrite, agentGrants);
        Assert.Contains(Permissions.KeyRead, agentGrants);
        Assert.Contains(Permissions.CustomerRead, agentGrants);
        Assert.Contains(Permissions.PaymentRead, agentGrants);
        Assert.Contains(Permissions.MailRead, agentGrants);
        Assert.Contains(Permissions.TelemetryRead, agentGrants);
        Assert.Contains(Permissions.SupportRead, agentGrants);
        Assert.Contains(Permissions.SupportWrite, agentGrants);
        Assert.Contains(Permissions.InvoiceRead, agentGrants);
    }

    [Fact]
    public void Every_granted_code_exists_in_the_catalog()
    {
        foreach (var grants in Permissions.DefaultRoleGrants.Values)
            Assert.All(grants, code => Assert.True(Permissions.Catalog.ContainsKey(code), $"{code} missing from catalog"));
    }
}
