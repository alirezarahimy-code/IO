using IO.Infrastructure.Security;
using System.Text.RegularExpressions;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name); count++;
}
string Resource(string name)
{
    using var stream = typeof(IoWebHost).Assembly.GetManifestResourceStream("IO.Web.Ui." + name)
        ?? throw new InvalidOperationException("Missing embedded resource " + name);
    using var reader = new StreamReader(stream); return reader.ReadToEnd();
}
Check(Environment.Version.Major == 10, ".NET 10 runtime");
Check(TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Tehran", out _), "IANA business time zone on current OS");
Check(PermissionCatalog.All.Count == PermissionCatalog.Codes.Count, "permission catalogue has unique ordinal codes");
Check(!PermissionCatalog.Codes.Contains("IDENTITY.MANAGE") && !PermissionCatalog.Codes.Contains("identity.manage "), "permission catalogue rejects case and whitespace variants");
Check(PermissionCatalog.Codes.Contains("leave.request.submit") && PermissionCatalog.Codes.Contains("leave.request.decide") && PermissionCatalog.Codes.Contains("correction.edit.self"), "previously omitted UI permissions are catalogued");
Check(PermissionCatalog.Operations.Values.All(p => PermissionCatalog.Codes.Contains(p.Permission) && PermissionCatalog.Codes.Contains(p.Legacy)), "every split operation and compatibility bundle is registered");
Check(PermissionCatalog.Resolve("organization.manage", "org.catalog.viewed").Permission == "organization.read", "read permission separated from organization writes");
Check(PermissionCatalog.Resolve("identity.manage", "permission-revoke.123").Permission == "identity.roles.manage", "dynamic audit target cannot bypass identity permission split");
Check(PermissionCatalog.Resolve("correction.submit.self", "correction.request.created").Permission == "correction.create.self" && !PermissionCatalog.Templates.Single(t => t.Key == "employee").Permissions.Contains("correction.submit.self"), "new employee template uses independent create instead of legacy submission bundle");
Check(PermissionCatalog.Templates.Count == 5 && PermissionCatalog.Templates.Select(t => t.Id).Distinct().Count() == 5, "five stable role templates");
Check(PermissionCatalog.Templates.All(t => t.Permissions.All(PermissionCatalog.Codes.Contains)), "templates grant only implemented catalogue permissions");
Check(PermissionCatalog.Templates.Single(t => t.Key == "operator").Permissions.SequenceEqual(new[] { "devices.files.read", "devices.users.read", "devices.info.read", "devices.connection.read", "devices.read", "devices.groups.read" }), "device operator grants only implemented registry read without hardware command rights");
Check(PermissionCatalog.Templates.Single(t => t.Key == "executive").Permissions.SequenceEqual(new[] { "executive.dashboard.read" }), "executive role has no administrator grant");
Check(WorkspaceCatalog.Pages.Count == 46 && WorkspaceCatalog.Pages.Select(p => p.Route).Distinct().Count() == 46, "existing pages, help and reversal catalogue");
Check(WorkspaceCatalog.Pages.All(p => p.AnyPermissions.All(PermissionCatalog.Codes.Contains)), "every page permission exists");
var executive = new HashSet<string>(new[] { "executive.dashboard.read" }, StringComparer.Ordinal);
Check(WorkspaceCatalog.Pages.Where(p => WorkspaceCatalog.CanView(p, executive) && p.Group != "common").Select(p => p.Route).SequenceEqual(new[] { "/workspace/executive-home" }), "executive exact menu excludes administration and team pages");
var empty = new HashSet<string>(StringComparer.Ordinal);
Check(!WorkspaceCatalog.CanView(WorkspaceCatalog.Pages.Single(p => p.Route == "/workspace/roles"), empty), "no-role account has no role administration menu");
var articles = SystemHelp.Parse(Resource("HelpContent.json"));
Check(articles.Count == 46 && articles.Select(a => a.Slug).Distinct().Count() == 46, "help has 46 unique articles");
Check(WorkspaceCatalog.Pages.All(p => articles.Any(a => a.Route == p.Route)), "every existing route has context help");
Check(articles.All(a => a.Steps.Length >= 3 && a.Errors.Length > 0 && a.Example.Length > 10 && a.Limitations.Length > 0), "articles include workflow example errors and limitations");
Check(articles.Where(a => SystemHelp.CanRead(a, false, empty)).Select(a => a.Route).SequenceEqual(new[] { "/login" }), "anonymous help contains only login article");
Check(!SystemHelp.CanRead(articles.Single(a => a.Slug == "roles"), true, executive), "executive cannot fetch protected role help directly");
Check(SystemHelp.CanRead(articles.Single(a => a.Slug == "executive-home"), true, executive) && !SystemHelp.CanRead(articles.Single(a => a.Slug == "executive-home"), true, empty), "help re-evaluates revoked permission");
Check(!Resource("help.js").Contains("innerHTML", StringComparison.Ordinal), "help renders content with safe DOM text operations");
Check(WorkspaceCatalog.Pages.All(p => Resource(p.Resource).Contains("/assets/theme.js", StringComparison.Ordinal)), "all pages carry context help toolbar");
Check(WorkspaceCatalog.Pages.All(p => !Regex.IsMatch(Resource(p.Resource), "<(?:script|link)[^>]+(?:src|href)=[\"']https?://")), "pages use local assets without CDN dependencies");
Check(PermissionCatalog.Codes.Contains("correction.reverse") && PermissionCatalog.Codes.Contains("leave.request.reverse"), "both reversal permissions are independently registered");
Check(PermissionCatalog.Templates.Where(t => t.Key is "manager" or "admin").All(t => t.Permissions.Contains("correction.reverse") && t.Permissions.Contains("leave.request.reverse")) && !PermissionCatalog.Templates.Single(t => t.Key == "employee").Permissions.Contains("correction.reverse"), "known managerial templates have reversal rights without self-service escalation");
Check(!Resource("reversals.js").Contains("innerHTML", StringComparison.Ordinal) && Resource("reversals.js").Contains("expectedDecisionId", StringComparison.Ordinal), "reversal UI safely renders evidence and binds expected decision");
Check(new[] { "devices.read", "devices.create", "devices.update", "devices.credentials.manage", "devices.receive", "devices.send", "devices.settings", "devices.sensitive" }.All(PermissionCatalog.Codes.Contains), "device rights are independent known grantable catalogue permissions");
Check(WorkspaceCatalog.CanView(WorkspaceCatalog.Pages.Single(p => p.Route == "/workspace/devices"), new HashSet<string> { "devices.read" }) && !WorkspaceCatalog.CanView(WorkspaceCatalog.Pages.Single(p => p.Route == "/workspace/devices"), new HashSet<string> { "attendance.ingest" }), "legacy attendance permission does not grant device page");
Check(!Resource("devices.js").Contains("innerHTML", StringComparison.Ordinal) && Resource("Devices.html").Contains("autocomplete=\"new-password\"", StringComparison.Ordinal), "device UI uses safe text and non-retrievable password input");
Check(PermissionCatalog.Templates.All(t => !t.Permissions.Intersect(new[] { "devices.receive", "devices.send", "devices.settings", "devices.sensitive" }).Any()), "no template grants unimplemented hardware commands");
Check(new[] { "devices.groups.read", "devices.groups.manage", "devices.groups.assign" }.All(PermissionCatalog.Codes.Contains), "reference group read manage and device assignment are separately grantable");
Check(!Resource("device-groups.js").Contains("innerHTML", StringComparison.Ordinal) && Resource("DeviceGroups.html").Contains("delete-form", StringComparison.Ordinal), "reference group UI renders safe text and requires explicit leaf deletion confirmation");
Check(!WorkspaceCatalog.CanView(WorkspaceCatalog.Pages.Single(p=>p.Route=="/workspace/device-groups"),new HashSet<string>{"devices.read"}), "device inventory read does not imply reference group access");
Check(PermissionCatalog.Templates.All(t=>!t.Permissions.Contains("devices.connection.execute")&&!t.Permissions.Contains("devices.operations.execute")&&!t.Permissions.Contains("devices.users.execute")), "service execution is never implicitly assigned to human templates");
Check(!Resource("device-connection.js").Contains("innerHTML", StringComparison.Ordinal) && Resource("DeviceConnection.html").Contains("tcp-reachable", StringComparison.Ordinal), "diagnostic page distinguishes transport reachability from authenticated model support");
Check(new[]{"devices.info.read","devices.clock.set","devices.operations.execute"}.All(PermissionCatalog.Codes.Contains), "S06 read clock and service execution permissions are independent");
Check(!Resource("device-information.js").Contains("innerHTML",StringComparison.Ordinal)&&Resource("DeviceInformation.html").Contains("resolve-form",StringComparison.Ordinal), "S06 typed snapshots render safe text and unknown clock results require explicit review");
Check(new[]{"devices.users.read","devices.users.download","devices.users.execute"}.All(PermissionCatalog.Codes.Contains)&&!Resource("device-users.js").Contains("innerHTML",StringComparison.Ordinal), "S07 separate grants and safe personal snapshot rendering are catalogued");
Check(new[]{"devices.files.read","devices.files.preview","devices.files.commit"}.All(PermissionCatalog.Codes.Contains)&&!Resource("device-files.js").Contains("innerHTML",StringComparison.Ordinal)&&Resource("DeviceFiles.html").Contains("commit-form",StringComparison.Ordinal), "S12 file read preview and confirmed commit permissions and safe database UI are separate");
Console.WriteLine($"PASS {count} foundation catalogue and offline-help checks on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
