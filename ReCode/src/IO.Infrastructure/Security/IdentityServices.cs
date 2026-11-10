using System.Data;
using IO.Infrastructure.Organization;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Workflows;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Scheduling;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Reports;
using IO.Infrastructure.Leaves;
using IO.Infrastructure.Delegation;
using IO.Infrastructure.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace IO.Infrastructure.Security;

public static class IdentityServices
{
    public const string MustChangePasswordClaim = "io.password_change_required";
    // Caller supplies an explicitly configured and authorized SQL connection.
    // This method does not create a database, apply migrations, or seed users.
    public static IServiceCollection AddIoIdentityStorage(this IServiceCollection services, string connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connection);
        services.AddDbContext<IoDbContext>(o => o.UseSqlServer(connection));
        services.AddDbContext<LoginIdentityDbContext>(o => o.UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__IdentityMigrationsHistory")));
        services.AddIdentityCore<LoginAccount>(options =>
        {
            options.User.RequireUniqueEmail = false;
            options.Password.RequiredLength = 14;
            options.Password.RequiredUniqueChars = 4;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<LoginIdentityDbContext>().AddSignInManager();
        services.AddScoped<InitialAdministrator>();
        services.AddScoped<IdentityAdministration>();
        services.AddScoped<PersonalCredentials>();
        services.AddScoped<PermissionTransaction>();
        services.AddScoped<OrganizationManagement>();
        services.AddScoped<AttendanceIngestion>();
        services.AddScoped<PersonalAttendance>();
        services.AddScoped<TeamAttendance>();
        services.AddScoped<ObservedCalculation>();
        services.AddScoped<PersonalReports>();
        services.AddScoped<ExecutiveOverview>();
        services.AddScoped<LeaveLedger>();
        services.AddScoped<LeaveRequests>();
        services.AddScoped<DelegationRegistry>();
        services.AddScoped<CorrectionSettings>();
        services.AddScoped<CorrectionWorkflow>();
        services.AddScoped<PeriodPreflight>();
        services.AddScoped<PeriodDrafts>();
        services.AddScoped<PeriodDiagnostics>();
        services.AddScoped<ScheduleManagement>();
        services.AddScoped<EmployeeAccessReader>();
        services.AddScoped<ScheduleReader>();
        return services;
    }
}

/// <summary>
/// One-time initializer, not an HTTP endpoint. Caller must control execution,
/// protect the credential, and provide an already migrated, authorized database.
/// </summary>
public sealed class InitialAdministrator(LoginIdentityDbContext identity, IoDbContext foundation, UserManager<LoginAccount> users)
{
    public const string ManageIdentity = "identity.manage";

    public async Task<Guid> CreateAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 200 || password is null || password.Length > 256)
            throw new ArgumentException("Invalid administrator input.");
        await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // Competing initializers serialize before looking for any existing account.
        await identity.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'IO.InitialAdministrator', @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51001, 'Unable to acquire bootstrap lock.', 1;
            """, cancellationToken);
        await foundation.Database.CloseConnectionAsync();
        foundation.Database.SetDbConnection(identity.Database.GetDbConnection());
        await foundation.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        if (await identity.Users.AnyAsync(cancellationToken) || await foundation.Users.AnyAsync(cancellationToken))
            throw new InvalidOperationException("The database already contains an account; initial administrator cannot be created.");

        var id = Guid.NewGuid();
        var login = new LoginAccount { Id = id, UserName = userName, LockoutEnabled = true };
        var result = await users.CreateAsync(login, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Administrator creation rejected by identity validation: " +
                string.Join(", ", result.Errors.Select(x => x.Code)));

        var roleId = Guid.NewGuid();
        var readId = Guid.NewGuid();
        var manageId = Guid.NewGuid();
        foundation.Users.Add(new UserRecord { Id = id, UserName = userName, IsActive = true });
        foundation.Roles.Add(new RoleRecord { Id = roleId, Name = "Initial administrator" });
        foundation.Permissions.AddRange(new PermissionRecord { Id = readId, Code = EmployeeAccessReader.ReadAll },
            new PermissionRecord { Id = manageId, Code = ManageIdentity });
        foundation.UserRoles.Add(new UserRoleRecord { UserId = id, RoleId = roleId });
        foundation.RolePermissions.AddRange(new RolePermissionRecord { RoleId = roleId, PermissionId = readId },
            new RolePermissionRecord { RoleId = roleId, PermissionId = manageId });
        identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = id,
            Action = "identity.initial-administrator.created", OccurredAtUtc = DateTimeOffset.UtcNow });
        await foundation.SaveChangesAsync(cancellationToken);
        await identity.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }
}
