using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Data;

public class Product
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string UserAgentPrefix { get; set; } = "";
    public string HmacSecret { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class License
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string PlanType { get; set; } = "1year";
    public int MaxMachines { get; set; } = 3;
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRevoked { get; set; }
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

    public DbSet<Product> Products => Set<Product>();
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
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.Property(p => p.Slug).HasMaxLength(64);
            entity.Property(p => p.DisplayName).HasMaxLength(200);
            entity.Property(p => p.AppKey).HasMaxLength(128);
            entity.Property(p => p.KeyPrefix).HasMaxLength(16);
            entity.Property(p => p.UserAgentPrefix).HasMaxLength(128);
            entity.Property(p => p.HmacSecret).HasMaxLength(512);
            entity.HasIndex(p => p.Slug).IsUnique();
            entity.HasIndex(p => p.AppKey).IsUnique();
        });

        modelBuilder.Entity<License>(entity =>
        {
            entity.ToTable("Licenses");
            entity.Property(l => l.Key).HasMaxLength(128);
            entity.Property(l => l.PlanType).HasMaxLength(32);
            entity.HasIndex(l => l.Key).IsUnique();
            entity.HasIndex(l => new { l.Key, l.ProductId });
            entity.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
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
