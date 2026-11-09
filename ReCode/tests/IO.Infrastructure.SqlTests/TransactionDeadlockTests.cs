using IO.Infrastructure.Calculation;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class TransactionDeadlockTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid actor; Guid[] units;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var adminRole = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            actor = await db.UserRoles.Where(x => x.RoleId == adminRole.Id).Select(x => x.UserId).SingleAsync();
            units = await db.OrgUnits.OrderBy(x => x.Id).Select(x => x.Id).Take(2).ToArrayAsync();
            if (units.Length != 2) throw new InvalidOperationException("Synthetic deadlock fixtures missing.");
        }
        var bothLocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var locked = 0; var target = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<bool> Attempt(Guid first, Guid second)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var transaction = scope.ServiceProvider.GetRequiredService<PermissionTransaction>();
            try
            {
                return await transaction.ExecuteAsync(actor, ObservedCalculation.Manage, "test.deadlock", async ct =>
                {
                    // Independent rows in an unmonitored table make a real SQL
                    // deadlock without contaminating calculation-input revision.
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrgUnits SET Name = Name WHERE Id = {first}", ct);
                    if (Interlocked.Increment(ref locked) == 2) bothLocked.TrySetResult(true);
                    await bothLocked.Task.WaitAsync(ct);
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OrgUnits SET Name = Name WHERE Id = {second}", ct);
                    return (true, target);
                }, timeout.Token);
            }
            catch (DomainConflictException) { return false; }
        }
        var results = await Task.WhenAll(Attempt(units[0], units[1]), Attempt(units[1], units[0]));
        await using (var scope = provider.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(results.Count(x => x) == 1 && await identity.SecurityAudit.CountAsync(x => x.Action == "test.deadlock:" + target.ToString("N")) == 1,
                "real SQL deadlock rolls back one transaction and returns a retryable conflict with one audit");
        }
        Guid employeeId; string originalName; var conflicted = false;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var transaction = scope.ServiceProvider.GetRequiredService<PermissionTransaction>();
            employeeId = await db.Employees.AsNoTracking().Select(x => x.Id).FirstAsync();
            originalName = await db.Employees.AsNoTracking().Where(x => x.Id == employeeId).Select(x => x.DisplayName).SingleAsync();
            try
            {
                await transaction.ExecuteAsync(actor, ObservedCalculation.Manage, "test.rowversion-conflict", async ct =>
                {
                    var employee = await db.Employees.SingleAsync(x => x.Id == employeeId, ct);
                    employee.DisplayName = "must not commit";
                    db.Entry(employee).Property(x => x.Version).OriginalValue = new byte[8];
                    return (true, employeeId);
                });
            }
            catch (DomainConflictException) { conflicted = true; }
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var identity = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(conflicted && await db.Employees.AsNoTracking().Where(x => x.Id == employeeId).Select(x => x.DisplayName).SingleAsync() == originalName &&
                  !await identity.SecurityAudit.AnyAsync(x => x.Action == "test.rowversion-conflict:" + employeeId.ToString("N")),
                  "optimistic concurrency failure returns conflict without committing data or audit");
        }
    }
}
