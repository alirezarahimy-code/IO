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
    public DbSet<SecurityAuditRecord> SecurityAudit => Set<SecurityAuditRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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
