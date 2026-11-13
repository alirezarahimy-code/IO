using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IO.Infrastructure.Security;

// Identity owns credentials and lockout state; authorization remains in IoDbContext.
// The IDs must be linked to active foundation users before granting any access.
public sealed class LoginAccount : IdentityUser<Guid>;

public sealed class SecurityAuditRecord
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class LoginIdentityDbContext(DbContextOptions<LoginIdentityDbContext> options)
    : IdentityUserContext<LoginAccount, Guid>(options)
{
    public DbSet<DeviceOperatorPolicy> DeviceOperatorPolicies => Set<DeviceOperatorPolicy>();
    public DbSet<DeviceOperatorGrant> DeviceOperatorGrants => Set<DeviceOperatorGrant>();
    public DbSet<SecurityAuditRecord> SecurityAudit => Set<SecurityAuditRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<DeviceOperatorPolicy>(b=>
        {
            b.ToTable("DeviceOperatorPolicies",t=>{t.HasTrigger("TR_DeviceOperatorPolicies_History");t.UseSqlOutputClause(false);t.HasCheckConstraint("CK_DeviceOperatorPolicies_Utc","DATEPART(TZOFFSET,[ChangedAtUtc])=0");});
            b.HasKey(x=>x.UserId);b.Property(x=>x.UserId).ValueGeneratedNever();b.Property(x=>x.Version).IsRowVersion();
        });
        builder.Entity<DeviceOperatorGrant>(b=>
        {
            b.ToTable("DeviceOperatorGrants",t=>{t.HasTrigger("TR_DeviceOperatorGrants_History");t.UseSqlOutputClause(false);t.HasCheckConstraint("CK_DeviceOperatorGrants_Utc","DATEPART(TZOFFSET,[GrantedAtUtc])=0 AND DATEPART(TZOFFSET,[RevokedAtUtc])=0 AND ([RevokedAtUtc] IS NULL OR [RevokedAtUtc]>=[GrantedAtUtc])");});
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.Property(x=>x.Version).IsRowVersion();
            b.HasIndex(x=>new{x.UserId,x.DeviceId}).IsUnique().HasFilter("[RevokedAtUtc] IS NULL");
        });
        builder.Entity<SecurityAuditRecord>(b =>
        {
            b.ToTable("SecurityAudit");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.ToTable(t => t.HasCheckConstraint("CK_SecurityAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0"));
        });
    }
}

// Scaffold/script generation only. Never used as an application connection.
public sealed class LoginIdentityDbContextFactory : IDesignTimeDbContextFactory<LoginIdentityDbContext>
{
    public LoginIdentityDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<LoginIdentityDbContext>()
            .UseSqlServer("Server=127.0.0.1;Database=IO_DesignOnly;Encrypt=True;TrustServerCertificate=False",
                sql => sql.MigrationsHistoryTable("__IdentityMigrationsHistory"))
            .Options);
}
