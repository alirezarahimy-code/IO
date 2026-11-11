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
Check(PermissionCatalog.Templates.Single(t => t.Key == "operator").Permissions.Length == 0, "device operator grants no unimplemented device rights");
Check(PermissionCatalog.Templates.Single(t => t.Key == "executive").Permissions.SequenceEqual(new[] { "executive.dashboard.read" }), "executive role has no administrator grant");
Check(WorkspaceCatalog.Pages.Count == 39 && WorkspaceCatalog.Pages.Select(p => p.Route).Distinct().Count() == 39, "38 existing pages plus help catalogue");
Check(WorkspaceCatalog.Pages.All(p => p.AnyPermissions.All(PermissionCatalog.Codes.Contains)), "every page permission exists");
var executive = new HashSet<string>(new[] { "executive.dashboard.read" }, StringComparer.Ordinal);
Check(WorkspaceCatalog.Pages.Where(p => WorkspaceCatalog.CanView(p, executive) && p.Group != "common").Select(p => p.Route).SequenceEqual(new[] { "/workspace/executive-home" }), "executive exact menu excludes administration and team pages");
var empty = new HashSet<string>(StringComparer.Ordinal);
Check(!WorkspaceCatalog.CanView(WorkspaceCatalog.Pages.Single(p => p.Route == "/workspace/roles"), empty), "no-role account has no role administration menu");
var articles = SystemHelp.Parse(Resource("HelpContent.json"));
Check(articles.Count == 39 && articles.Select(a => a.Slug).Distinct().Count() == 39, "help has 39 unique articles");
Check(WorkspaceCatalog.Pages.All(p => articles.Any(a => a.Route == p.Route)), "every existing route has context help");
Check(articles.All(a => a.Steps.Length >= 3 && a.Errors.Length > 0 && a.Example.Length > 10 && a.Limitations.Length > 0), "articles include workflow example errors and limitations");
Check(articles.Where(a => SystemHelp.CanRead(a, false, empty)).Select(a => a.Route).SequenceEqual(new[] { "/login" }), "anonymous help contains only login article");
Check(!SystemHelp.CanRead(articles.Single(a => a.Slug == "roles"), true, executive), "executive cannot fetch protected role help directly");
Check(SystemHelp.CanRead(articles.Single(a => a.Slug == "executive-home"), true, executive) && !SystemHelp.CanRead(articles.Single(a => a.Slug == "executive-home"), true, empty), "help re-evaluates revoked permission");
Check(!Resource("help.js").Contains("innerHTML", StringComparison.Ordinal), "help renders content with safe DOM text operations");
Check(WorkspaceCatalog.Pages.All(p => Resource(p.Resource).Contains("/assets/theme.js", StringComparison.Ordinal)), "all pages carry context help toolbar");
Check(WorkspaceCatalog.Pages.All(p => !Regex.IsMatch(Resource(p.Resource), "<(?:script|link)[^>]+(?:src|href)=[\"']https?://")), "pages use local assets without CDN dependencies");
Console.WriteLine($"PASS {count} foundation catalogue and offline-help checks on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
