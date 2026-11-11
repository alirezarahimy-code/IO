using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Global administrative inventory permissions, never live device commands.</summary>
public sealed class DeviceRegistry(IoDbContext db, PermissionTransaction transaction, IDataProtectionProvider protection)
{
    public const string Read = "devices.read", Create = "devices.create", Update = "devices.update", Credentials = "devices.credentials.manage";
    public sealed record Input(Guid SiteId, string Number, string Name, string Model, string Profile, string? Host, int? Port, bool Enabled);
    public sealed record DeviceView
    {
        public Guid Id { get; init; }
        public Guid? GroupId { get; init; }
        public string? GroupName { get; init; }
        public Guid SiteId { get; init; }
        public Guid CompanyId { get; init; }
        public Guid SourceId { get; init; }
        public string Number { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string Model { get; init; } = null!;
        public string Profile { get; init; } = null!;
        public string? Host { get; init; }
        public int? Port { get; init; }
        public bool Enabled { get; init; }
        public bool HasCredential { get; init; }
        public byte[] Version { get; init; } = null!;
        public string ConnectionStatus { get; init; } = "not-tested";
    }
    private static readonly string[] CommandPermissions = ["devices.receive", "devices.send", "devices.settings", "devices.sensitive"];

    public Task<Guid> CreateAsync(Guid actor, Guid operationId, Input input, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Create, "devices.created", async token =>
        {
            var value = Validate(input);
            if (operationId == Guid.Empty) throw new ArgumentException("Operation id required.");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
            // All registry writes use this lock after permission checks. Unique indexes
            // remain the final guard against writes bypassing the application service.
            await transaction.LockAsync("IO.DeviceRegistry", token);
            var existing = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == operationId, token);
            if (existing is not null)
            {
                if (existing.CreatedByUserId != actor || existing.CreationFingerprint != fingerprint) throw new DomainConflictException("Changed create replay.");
                return (existing.Id, existing.Id);
            }
            await EnsureSite(value.SiteId, token);
            await EnsureNumber(value.Number, operationId, token);
            if (await db.EventSources.AnyAsync(s => s.Id == operationId, token)) throw new DomainConflictException("Identity already used by a source.");
            db.EventSources.Add(new() { Id = operationId, SiteId = value.SiteId, Name = value.Name });
            var now = DateTimeOffset.UtcNow;
            db.Devices.Add(new() { Id = operationId, SourceId = operationId, SiteId = value.SiteId, Number = value.Number, Name = value.Name,
                Model = value.Model, Profile = value.Profile, Host = value.Host, Port = value.Port, Enabled = value.Enabled,
                CreationFingerprint = fingerprint, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
            return (operationId, operationId);
        }, ct);

    public Task<Guid> UpdateAsync(Guid actor, Guid id, byte[] expectedVersion, Input input, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Update, "devices.updated", async token =>
        {
            var value = Validate(input);
            await transaction.LockAsync("IO.DeviceRegistry", token);
            var device = await LoadForWrite(id, expectedVersion, token);
            await EnsureSite(value.SiteId, token); await EnsureNumber(value.Number, id, token);
            if(device.GroupId is not null && !await (from g in db.DeviceGroups join site in db.Sites on g.CompanyId equals site.CompanyId
                where g.Id==device.GroupId && g.DeletedAtUtc==null && site.Id==value.SiteId select g.Id).AnyAsync(token))
                throw new DomainConflictException("Detach the device group before moving to a different company.");
            if ((device.Profile != value.Profile || device.Host != value.Host || device.Port != value.Port) &&
                await db.DeviceSecrets.AnyAsync(s => s.DeviceId == id, token))
                throw new DomainConflictException("Remove the stored credential explicitly before retargeting the connection profile.");
            if (device.SiteId != value.SiteId &&
                (await db.RawAttendanceEvents.AnyAsync(e => e.SourceId == device.SourceId, token) ||
                 await db.SourceEmployeeMappings.AnyAsync(m => m.SourceId == device.SourceId, token) ||
                 await db.RawEventConflicts.AnyAsync(e => e.SourceId == device.SourceId, token)))
                throw new DomainConflictException("Source history prevents moving this device; disable it and create a new registration.");
            var source = await db.EventSources.AsTracking().SingleAsync(s => s.Id == device.SourceId, token);
            source.SiteId = value.SiteId; source.Name = value.Name;
            device.SiteId = value.SiteId; device.Number = value.Number; device.Name = value.Name; device.Model = value.Model;
            device.Profile = value.Profile; device.Host = value.Host; device.Port = value.Port; device.Enabled = value.Enabled;
            device.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return (id, id);
        }, ct);

    public Task<Guid> SetCredentialAsync(Guid actor, Guid id, byte[] expectedVersion, string? secret, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Credentials, "devices.credential.changed", async token =>
        {
            if (secret is not null && (secret.Length is < 1 or > 128 || secret.Any(char.IsControl))) throw new ArgumentException("Invalid credential.");
            await transaction.LockAsync("IO.DeviceRegistry", token);
            var device = await LoadForWrite(id, expectedVersion, token);
            var stored = await db.DeviceSecrets.AsTracking().SingleOrDefaultAsync(s => s.DeviceId == id, token);
            if (secret is null) { if (stored is not null) db.DeviceSecrets.Remove(stored); }
            else
            {
                // Ciphertext is bound to this application, purpose and device id.
                var ciphertext = protection.CreateProtector("IO.Devices.Credential.v1", id.ToString("N")).Protect(secret);
                if (stored is null) db.DeviceSecrets.Add(new() { DeviceId = id, ProtectedValue = ciphertext });
                else stored.ProtectedValue = ciphertext;
            }
            device.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return (id, id);
        }, ct);

    public Task<object> ListAsync(Guid actor, Guid? companyId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Read, "devices.viewed", async token =>
        {
            if (offset is < 0 or > 100000 || companyId == Guid.Empty) throw new ArgumentException("Invalid filter.");
            var items = await Views().Where(d => companyId == null || d.CompanyId == companyId).OrderBy(d => d.Number).ThenBy(d => d.Id)
                .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> GetAsync(Guid actor, Guid id, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Read, "devices.viewed", async token =>
        {
            var item = await Views().SingleOrDefaultAsync(d => d.Id == id, token) ?? throw new ArgumentException("Unknown device.");
            return (item, id);
        }, ct);

    public Task<object> LocationsAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteEitherAsync<object>(actor, Read, Create, "devices.locations.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await (from s in db.Sites join c in db.Companies on s.CompanyId equals c.Id orderby c.Name, s.Name, s.Id
                select new { s.Id, s.CompanyId, CompanyName = c.Name, s.Name, s.TimeZoneId }).Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> CapabilitiesAsync(Guid actor, Guid id, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Read, "devices.capabilities.viewed", async token =>
        {
            if (!await db.Devices.AnyAsync(d => d.Id == id, token)) throw new ArgumentException("Unknown device.");
            var operations = new List<object>();
            foreach (var permission in CommandPermissions)
                operations.Add(new { permission, authorized = await transaction.HasPermissionAsync(actor, permission, token), available = false,
                    reason = "Adapter and model not verified; no command will be sent in S04." });
            return (new { id, operations }, id);
        }, ct);

    private IQueryable<DeviceView> Views() => from d in db.Devices.AsNoTracking() join s in db.Sites on d.SiteId equals s.Id
        select new DeviceView { Id=d.Id, GroupId=d.GroupId, GroupName=db.DeviceGroups.Where(g=>g.Id==d.GroupId).Select(g=>g.Name).FirstOrDefault(), SiteId=d.SiteId, CompanyId=s.CompanyId, SourceId=d.SourceId, Number=d.Number, Name=d.Name,
            Model=d.Model, Profile=d.Profile, Host=d.Host, Port=d.Port, Enabled=d.Enabled,
            HasCredential=db.DeviceSecrets.Any(x => x.DeviceId == d.Id), Version=d.Version, ConnectionStatus="not-tested" };
    private async Task<DeviceRecord> LoadForWrite(Guid id, byte[] version, CancellationToken ct)
    {
        if (version is null || version.Length != 8) throw new ArgumentException("Rowversion required.");
        var device = await db.Devices.AsTracking().SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new ArgumentException("Unknown device.");
        if (!device.Version.SequenceEqual(version)) throw new DomainConflictException("Stale version.");
        db.Entry(device).Property(d => d.Version).OriginalValue = version;
        return device;
    }
    private async Task EnsureSite(Guid site, CancellationToken ct)
    { if (!await db.Sites.AnyAsync(s => s.Id == site, ct)) throw new ArgumentException("Unknown site."); }
    private async Task EnsureNumber(string number, Guid id, CancellationToken ct)
    { if (await db.Devices.AnyAsync(d => d.Number == number && d.Id != id, ct)) throw new DomainConflictException("Device number is already registered."); }
    private static Input Validate(Input value)
    {
        if (value is null || value.SiteId == Guid.Empty) throw new ArgumentException("Site required.");
        var number = value.Number?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(number, "^[A-Z0-9][A-Z0-9_-]{0,39}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid number.");
        static string Text(string? s, int max)
        { if (string.IsNullOrWhiteSpace(s) || s.Length > max || s.Any(char.IsControl)) throw new ArgumentException("Invalid label."); return s.Trim(); }
        var name = Text(value.Name, 200); var model = Text(value.Model, 120);
        if (value.Profile == "zk-standalone")
        {
            if (string.IsNullOrWhiteSpace(value.Host) || value.Host != value.Host.Trim() || value.Host.Length > 253 ||
                Uri.CheckHostName(value.Host) == UriHostNameType.Unknown || value.Port is null or < 1 or > 65535)
                throw new ArgumentException("A plain host and valid port are required (not a URL or credential).");
        }
        else if (value.Profile is not ("zk-push" or "offline-file") || value.Host is not null || value.Port is not null)
            throw new ArgumentException("Unsupported profile or unexpected endpoint.");
        return value with { Number = number, Name = name, Model = model };
    }
}
