using System.Text.Json;

namespace IO.Infrastructure.Security;

public sealed record HelpField(string Name, string Label, string Description);
public sealed record HelpArticle(string Slug, string Route, string Title, string Group, string Overview,
    string[] Steps, string Example, HelpField[] Fields, string[] Actions, string[] Errors, string[] Limitations);

public static class SystemHelp
{
    public static bool CanRead(HelpArticle article, bool signedIn, IReadOnlySet<string> permissions)
    {
        if (article.Route == "/login") return true;
        var page = WorkspaceCatalog.Pages.SingleOrDefault(p => p.Route == article.Route);
        return signedIn && page is not null && WorkspaceCatalog.CanView(page, permissions);
    }
    public static IReadOnlyList<HelpArticle> Parse(string json) =>
        JsonSerializer.Deserialize<HelpArticle[]>(json) ?? throw new InvalidOperationException("Missing help content.");
}
