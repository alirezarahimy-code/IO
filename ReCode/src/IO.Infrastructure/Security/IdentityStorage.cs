using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IO.Infrastructure.Security;

// Identity owns credentials and lockout state; authorization remains in IoDbContext.
// The IDs must be linked to active foundation users before granting any access.
public sealed class LoginAccount : IdentityUser<Guid>;

public sealed class LoginIdentityDbContext(DbContextOptions<LoginIdentityDbContext> options)
    : IdentityUserContext<LoginAccount, Guid>(options);

// Scaffold/script generation only. Never used as an application connection.
public sealed class LoginIdentityDbContextFactory : IDesignTimeDbContextFactory<LoginIdentityDbContext>
{
    public LoginIdentityDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<LoginIdentityDbContext>()
            .UseSqlServer("Server=127.0.0.1;Database=IO_DesignOnly;Encrypt=True;TrustServerCertificate=False",
                sql => sql.MigrationsHistoryTable("__IdentityMigrationsHistory"))
            .Options);
}
