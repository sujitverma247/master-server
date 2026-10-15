namespace MasterServer.Data;

/// <summary>A buyer. Email is the unique identity; licenses and policy acceptances hang off this row.</summary>
public class Customer
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Source { get; set; } = "";
    /// <summary>Mail language: "en" or "de", from checkout/OS language family.</summary>
    public string Locale { get; set; } = "";
    public string? ExternalCustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<License> Licenses { get; set; } = new List<License>();
    public ICollection<RefundPolicyAcceptance> Acceptances { get; set; } = new List<RefundPolicyAcceptance>();
}

/// <summary>A published refund-policy version. Exactly one row should have IsCurrent = true.</summary>
public class RefundPolicy
{
    public int Id { get; set; }
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsCurrent { get; set; }
    public DateTime EffectiveAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A customer accepted a specific policy version. Unique per (customer, policy).</summary>
public class RefundPolicyAcceptance
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int PolicyId { get; set; }
    public RefundPolicy Policy { get; set; } = null!;
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Source { get; set; } = "";
}
