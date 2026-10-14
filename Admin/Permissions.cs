namespace MasterServer.Admin;

/// <summary>
/// The catalog of granular permission codes. Permissions are seeded into the database from this
/// list, and each role is granted a subset. Later phases add their codes here; enforcement only
/// uses codes that exist.
/// </summary>
public static class Permissions
{
    // Identity / RBAC (Phase 1)
    public const string UserManage = "user.manage";
    public const string AuditRead = "audit.read";

    // Catalog (Phase 2)
    public const string ProductRead = "product.read";
    public const string ProductWrite = "product.write";
    public const string PlanWrite = "plan.write";

    // Keys (Phase 3)
    public const string KeyRead = "key.read";
    public const string KeyIssue = "key.issue";
    public const string KeyRevoke = "key.revoke";

    // Customers (Phase 4)
    public const string CustomerRead = "customer.read";
    public const string CustomerWrite = "customer.write";

    // Payments (Phase 5)
    public const string PaymentRead = "payment.read";

    public static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        [UserManage] = "Create, update, and deactivate panel users.",
        [AuditRead] = "Read the audit log.",
        [ProductRead] = "View products and plans.",
        [ProductWrite] = "Create and edit products.",
        [PlanWrite] = "Create and edit plans.",
        [KeyRead] = "Look up license keys and activations.",
        [KeyIssue] = "Issue and generate license keys.",
        [KeyRevoke] = "Revoke, update, or delete license keys.",
        [CustomerRead] = "View customer profiles.",
        [CustomerWrite] = "Edit customer profiles.",
        [PaymentRead] = "View payments and order outcomes."
    };

    public const string RoleAdmin = "admin";
    public const string RoleAgent = "agent";

    /// <summary>Default role to permission grants. Admin gets everything; agent is read-only triage.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRoleGrants = new Dictionary<string, string[]>
    {
        [RoleAdmin] = Catalog.Keys.ToArray(),
        [RoleAgent] = new[] { AuditRead, ProductRead, KeyRead, CustomerRead, PaymentRead }
    };
}
