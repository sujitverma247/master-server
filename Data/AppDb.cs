using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Data;

public class Brand
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SupportEmail { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Product
{
    public int Id { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string UserAgentPrefix { get; set; } = "";
    public string HmacSecret { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A sellable plan. PlanType strings on licenses map to a Plan by Code (optional catalog).</summary>
public class Plan
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int DurationDays { get; set; }
    public int DefaultMaxMachines { get; set; } = 3;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A generated batch of keys. Lets admins revoke or export a whole production run.</summary>
public class KeyBatch
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ProductId { get; set; }
    public string PlanType { get; set; } = "1year";
    public int Quantity { get; set; }
    public int MaxMachines { get; set; } = 3;
    public string Notes { get; set; } = "";
    public string GeneratedBy { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class License
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? BatchId { get; set; }
    public KeyBatch? Batch { get; set; }
    public string PlanType { get; set; } = "1year";
    public int MaxMachines { get; set; } = 3;
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRevoked { get; set; }
    public string Notes { get; set; } = "";

    // Gateway-agnostic origin. "" = manually generated / retail. Otherwise the gateway ("freemius", ...).
    public string Source { get; set; } = "";
    public bool IsSubscription { get; set; }
    public string? ExternalSubscriptionId { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? ExternalCustomerId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public ICollection<Activation> Activations { get; set; } = new List<Activation>();
}

public class Activation
{
    public int Id { get; set; }
    public int LicenseId { get; set; }
    public License License { get; set; } = null!;
    public string MachineId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
}

public class UsedNonce
{
    public long Id { get; set; }
    public string Nonce { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<KeyBatch> KeyBatches => Set<KeyBatch>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<RefundPolicy> RefundPolicies => Set<RefundPolicy>();
    public DbSet<RefundPolicyAcceptance> RefundPolicyAcceptances => Set<RefundPolicyAcceptance>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<GatewayPlan> GatewayPlans => Set<GatewayPlan>();
    public DbSet<OutboundMail> MailOutbox => Set<OutboundMail>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Activation> Activations => Set<Activation>();
    public DbSet<UsedNonce> UsedNonces => Set<UsedNonce>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("Brands");
            entity.Property(b => b.Slug).HasMaxLength(64);
            entity.Property(b => b.Name).HasMaxLength(200);
            entity.Property(b => b.SupportEmail).HasMaxLength(256);
            entity.Property(b => b.WebsiteUrl).HasMaxLength(512);
            entity.HasIndex(b => b.Slug).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.Property(p => p.Slug).HasMaxLength(64);
            entity.Property(p => p.DisplayName).HasMaxLength(200);
            entity.Property(p => p.AppKey).HasMaxLength(128);
            entity.Property(p => p.KeyPrefix).HasMaxLength(16);
            entity.Property(p => p.UserAgentPrefix).HasMaxLength(128);
            entity.Property(p => p.HmacSecret).HasMaxLength(512);
            entity.Property(p => p.LatestVersion).HasMaxLength(64);
            entity.Property(p => p.DownloadUrl).HasMaxLength(512);
            entity.HasIndex(p => p.Slug).IsUnique();
            entity.HasIndex(p => p.AppKey).IsUnique();
            entity.HasOne(p => p.Brand)
                .WithMany()
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.ToTable("Plans");
            entity.Property(p => p.Code).HasMaxLength(32);
            entity.Property(p => p.DisplayName).HasMaxLength(200);
            entity.Property(p => p.Currency).HasMaxLength(8);
            entity.Property(p => p.Price).HasColumnType("numeric(12,2)");
            entity.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<KeyBatch>(entity =>
        {
            entity.ToTable("KeyBatches");
            entity.Property(b => b.Name).HasMaxLength(200);
            entity.Property(b => b.PlanType).HasMaxLength(32);
            entity.Property(b => b.Notes).HasMaxLength(1024);
            entity.Property(b => b.GeneratedBy).HasMaxLength(128);
            entity.HasIndex(b => b.ProductId);
        });

        modelBuilder.Entity<License>(entity =>
        {
            entity.ToTable("Licenses");
            entity.Property(l => l.Key).HasMaxLength(128);
            entity.Property(l => l.PlanType).HasMaxLength(32);
            entity.Property(l => l.Notes).HasMaxLength(1024);
            entity.Property(l => l.Source).HasMaxLength(32);
            entity.Property(l => l.ExternalSubscriptionId).HasMaxLength(128);
            entity.Property(l => l.ExternalPaymentId).HasMaxLength(128);
            entity.Property(l => l.ExternalCustomerId).HasMaxLength(128);
            entity.Property(l => l.CustomerEmail).HasMaxLength(256);
            entity.HasIndex(l => l.Key).IsUnique();
            entity.HasIndex(l => new { l.Key, l.ProductId });
            entity.HasIndex(l => l.CustomerEmail);
            entity.HasIndex(l => l.ExternalSubscriptionId);
            entity.HasIndex(l => l.BatchId);
            entity.HasIndex(l => l.CustomerId);
            entity.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(l => l.Batch)
                .WithMany()
                .HasForeignKey(l => l.BatchId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(l => l.Customer)
                .WithMany(c => c.Licenses)
                .HasForeignKey(l => l.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.Property(c => c.Email).HasMaxLength(256);
            entity.Property(c => c.DisplayName).HasMaxLength(200);
            entity.Property(c => c.Notes).HasMaxLength(1024);
            entity.Property(c => c.Source).HasMaxLength(32);
            entity.Property(c => c.ExternalCustomerId).HasMaxLength(128);
            entity.HasIndex(c => c.Email).IsUnique();
            entity.HasIndex(c => c.ExternalCustomerId);
        });

        modelBuilder.Entity<RefundPolicy>(entity =>
        {
            entity.ToTable("RefundPolicies");
            entity.Property(p => p.Version).HasMaxLength(64);
            entity.Property(p => p.Title).HasMaxLength(200);
            entity.HasIndex(p => p.Version).IsUnique();
            entity.HasIndex(p => p.IsCurrent);
        });

        modelBuilder.Entity<RefundPolicyAcceptance>(entity =>
        {
            entity.ToTable("RefundPolicyAcceptances");
            entity.Property(a => a.IpAddress).HasMaxLength(64);
            entity.Property(a => a.UserAgent).HasMaxLength(256);
            entity.Property(a => a.Source).HasMaxLength(32);
            entity.HasIndex(a => new { a.CustomerId, a.PolicyId }).IsUnique();
            entity.HasOne(a => a.Customer)
                .WithMany(c => c.Acceptances)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.Policy)
                .WithMany()
                .HasForeignKey(a => a.PolicyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.Property(o => o.PublicId).HasMaxLength(64);
            entity.Property(o => o.IdempotencyKey).HasMaxLength(128);
            entity.Property(o => o.PlanType).HasMaxLength(32);
            entity.Property(o => o.Currency).HasMaxLength(8);
            entity.Property(o => o.Gateway).HasMaxLength(32);
            entity.Property(o => o.Status).HasMaxLength(16);
            entity.Property(o => o.Amount).HasColumnType("numeric(12,2)");
            entity.Property(o => o.ExternalCheckoutId).HasMaxLength(128);
            entity.Property(o => o.ExternalPaymentId).HasMaxLength(128);
            entity.Property(o => o.ExternalSubscriptionId).HasMaxLength(128);
            entity.Property(o => o.CheckoutUrl).HasMaxLength(512);
            entity.HasIndex(o => o.PublicId).IsUnique();
            entity.HasIndex(o => o.IdempotencyKey).IsUnique();
            entity.HasIndex(o => o.CustomerId);
            entity.HasIndex(o => o.Status);
            entity.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.License)
                .WithMany()
                .HasForeignKey(o => o.LicenseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.ToTable("PaymentEvents");
            entity.Property(e => e.Gateway).HasMaxLength(32);
            entity.Property(e => e.ExternalEventId).HasMaxLength(128);
            entity.Property(e => e.EventType).HasMaxLength(64);
            entity.Property(e => e.PayloadSha256).HasMaxLength(64);
            entity.HasIndex(e => new { e.Gateway, e.ExternalEventId }).IsUnique();
            entity.HasIndex(e => e.OrderId);
        });

        modelBuilder.Entity<GatewayPlan>(entity =>
        {
            entity.ToTable("GatewayPlans");
            entity.Property(m => m.Gateway).HasMaxLength(32);
            entity.Property(m => m.PlanCode).HasMaxLength(32);
            entity.Property(m => m.ExternalPlanId).HasMaxLength(128);
            entity.HasIndex(m => new { m.Gateway, m.ProductId, m.PlanCode }).IsUnique();
        });

        modelBuilder.Entity<OutboundMail>(entity =>
        {
            entity.ToTable("MailOutbox");
            entity.Property(m => m.Template).HasMaxLength(64);
            entity.Property(m => m.IdempotencyKey).HasMaxLength(128);
            entity.Property(m => m.ToEmail).HasMaxLength(256);
            entity.Property(m => m.ToName).HasMaxLength(200);
            entity.Property(m => m.FromEmail).HasMaxLength(256);
            entity.Property(m => m.FromName).HasMaxLength(200);
            entity.Property(m => m.Subject).HasMaxLength(300);
            entity.Property(m => m.Status).HasMaxLength(16);
            entity.Property(m => m.Provider).HasMaxLength(32);
            entity.Property(m => m.ProviderMessageId).HasMaxLength(128);
            entity.Property(m => m.LastError).HasMaxLength(512);
            entity.HasIndex(m => m.IdempotencyKey).IsUnique();
            entity.HasIndex(m => m.Status);
            entity.HasIndex(m => m.ToEmail);
            entity.HasIndex(m => m.OrderId);
        });

        modelBuilder.Entity<Activation>(entity =>
        {
            entity.ToTable("Activations");
            entity.Property(a => a.MachineId).HasMaxLength(128);
            entity.Property(a => a.IpAddress).HasMaxLength(64);
            entity.Property(a => a.AppVersion).HasMaxLength(64);
            entity.Property(a => a.OsVersion).HasMaxLength(128);
            entity.HasIndex(a => new { a.LicenseId, a.MachineId });
            entity.HasOne(a => a.License)
                .WithMany(l => l.Activations)
                .HasForeignKey(a => a.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsedNonce>(entity =>
        {
            entity.ToTable("UsedNonces");
            entity.Property(n => n.Nonce).HasMaxLength(128);
            entity.HasIndex(n => n.Nonce).IsUnique();
            entity.HasIndex(n => n.CreatedAt);
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("AdminUsers");
            entity.Property(u => u.Username).HasMaxLength(128);
            entity.Property(u => u.PasswordHash).HasMaxLength(256);
            entity.Property(u => u.Role).HasMaxLength(64);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<AdminSession>(entity =>
        {
            entity.ToTable("AdminSessions");
            entity.Property(s => s.TokenHash).HasMaxLength(128);
            entity.HasIndex(s => s.TokenHash).IsUnique();
            entity.HasIndex(s => s.ExpiresAt);
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.Property(p => p.Code).HasMaxLength(64);
            entity.Property(p => p.Description).HasMaxLength(256);
            entity.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.Property(r => r.Role).HasMaxLength(64);
            entity.Property(r => r.PermissionCode).HasMaxLength(64);
            entity.HasIndex(r => new { r.Role, r.PermissionCode }).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.Property(a => a.Username).HasMaxLength(128);
            entity.Property(a => a.Role).HasMaxLength(64);
            entity.Property(a => a.Action).HasMaxLength(64);
            entity.Property(a => a.Target).HasMaxLength(256);
            entity.Property(a => a.Details).HasMaxLength(1024);
            entity.Property(a => a.IpAddress).HasMaxLength(64);
            entity.HasIndex(a => a.CreatedAt);
        });
    }
}
