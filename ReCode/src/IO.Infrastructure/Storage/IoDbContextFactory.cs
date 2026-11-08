using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IO.Infrastructure.Storage;

// For scaffolding only. No connection is opened and no application is started.
// Database updates must use an explicitly configured, authorized environment.
public sealed class IoDbContextFactory : IDesignTimeDbContextFactory<IoDbContext>
{
    public IoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IoDbContext>()
            .UseSqlServer("Server=127.0.0.1;Database=IO_DesignOnly;Encrypt=True;TrustServerCertificate=False")
            .Options;
        return new IoDbContext(options);
    }
}
