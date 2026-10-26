using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Inventory, NOT a port: Roslyn handles C#, and separate readers handle Razor,
// projects/config, SQL, and assembly metadata. Nothing loads a vendor DLL into
// the process. Config values and possible database contents are NEVER exported.
internal static class Program
{
    private sealed record Row(string Id, string System, string Kind, string Source,
        string Signature, string Sha256, string Line, string LegacyReference,
        string Destination, string Status, string Evidence, string Notes);
    private sealed record Scope(string Path, string System, string Role);
    private static readonly List<Row> Rows = new();
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal);
    private static readonly Regex SqlWords = new(@"\b(SELECT|INSERT|UPDATE|DELETE|EXEC(?:UTE)?|MERGE|JOIN)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SqlLine = new(@"(?im)^\s*(CREATE|ALTER|DROP|INSERT|UPDATE|DELETE|EXEC(?:UTE)?|GRANT|DENY|SELECT|MERGE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ViewLinks = new(@"Url\.Action\(\s*""([^""\r\n]+)""\s*,\s*""([^""\r\n]+)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static string _root = "";
    private static int _currentParseErrors;

    private static int Main(string[] args)
    {
        if (args.Length != 2 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: LedgerGenerator <repository-root> <output-csv>");
            return 2;
        }
        _root = Path.GetFullPath(args[0]);
        var scopes = new[]
        {
            new Scope("src/TWF", "ARIA", "CURRENT"),
            new Scope("TWF 4.28/App", "ARIA", "ORIGINAL"),
            new Scope("src/T5Web", "ARIA_ADMIN", "CURRENT"),
            new Scope("T5Web [3.5.7]/App", "ARIA_ADMIN", "ORIGINAL"),
            new Scope("T5V3.9.2", "ARIA_ADMIN", "SUPPLEMENTARY_DESKTOP"),
            new Scope("Link5 v4.0.4", "EXTERNAL_LINK5", "OUT_OF_SCOPE"),
        };
        foreach (var scope in scopes)
        {
            var directory = Path.Combine(_root, scope.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Required reference is missing: {scope.Path}");
            foreach (var full in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                string path = Relative(full);
                if (IsGenerated(path, scope.Role)) continue;
                Scan(full, path, scope);
            }
        }
        ReadLegacyActionDecisions();
        var ordered = Rows.OrderBy(x => x.System, StringComparer.Ordinal)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Line.PadLeft(8, '0'), StringComparer.Ordinal)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("ID,system,kind,source,signature,source_sha256,line,legacy_reference,destination,status,evidence,notes");
            foreach (var row in ordered)
                writer.WriteLine(string.Join(",", new[] { row.Id, row.System, row.Kind, row.Source,
                    row.Signature, row.Sha256, row.Line, row.LegacyReference, row.Destination,
                    row.Status, row.Evidence, row.Notes }.Select(Csv)));
        }
        foreach (var group in Rows.GroupBy(x => (x.System, x.Kind)).OrderBy(x => x.Key.System)
                     .ThenBy(x => x.Key.Kind))
            Console.WriteLine($"{group.Key.System,-15} {group.Key.Kind,-20} {group.Count(),5}");
        Console.WriteLine($"Rows: {Rows.Count}, distinct IDs: {Ids.Count}, current C# parser gaps: {_currentParseErrors}; output: {output}");
        // A failed parse must NOT look like a complete ledger. The partial file can
        // be inspected, but CI must fail and release coverage remains BLOCKED.
        return _currentParseErrors == 0 ? 0 : 1;
    }

    private static string Relative(string full) => Path.GetRelativePath(_root, full).Replace('\\', '/');
    private static bool IsGenerated(string path, string role)
    {
        var segments = path.Split('/');
        if (segments.Any(x => x is ".git" or ".venv" or "obj" or "build" or "node_modules")) return true;
        if (role == "CURRENT" && segments.Contains("bin")) return true;
        // Exclude this inventory's own outputs so it does not inventory itself.
        if (path.StartsWith("src/net10/", StringComparison.Ordinal)) return true;
        return false;
    }
    private static bool IsCurrentProductSource(string path) =>
        path.StartsWith("src/TWF/TD5/", StringComparison.Ordinal) ||
        path.StartsWith("src/TWF/TWF.Domain/", StringComparison.Ordinal) ||
        path.StartsWith("src/TWF/TWF.Application/", StringComparison.Ordinal) ||
        path.StartsWith("src/TWF/TWF.Infrastructure/", StringComparison.Ordinal) ||
        path.StartsWith("src/TWF/TWF.Web/", StringComparison.Ordinal) ||
        path.StartsWith("src/TWF/TWF.Tests/", StringComparison.Ordinal) ||
        path.StartsWith("src/T5Web/T5Web.Domain/", StringComparison.Ordinal) ||
        path.StartsWith("src/T5Web/T5Web.Application/", StringComparison.Ordinal) ||
        path.StartsWith("src/T5Web/T5Web.Data/", StringComparison.Ordinal) ||
        path.StartsWith("src/T5Web/T5Web.Web/", StringComparison.Ordinal) ||
        path.StartsWith("src/T5Web/T5Web.Tests/", StringComparison.Ordinal);
    private static string Legacy(string system) => system switch
    {
        "ARIA" => "TWF 4.28/App/",
        "ARIA_ADMIN" => "T5Web [3.5.7]/App/",
        _ => ""
    };
    private static void Add(string system, string kind, string source, string signature,
        string sha, string line, string status, string notes, string legacy = "", string destination = "")
    {
        var unique = $"{system}|{kind}|{source}|{signature}";
        string id = $"{system}:{kind}:{HashText(unique)[..16]}";
        if (!Ids.Add(id)) throw new InvalidOperationException($"Duplicate inventory identity: {unique}");
        Rows.Add(new Row(id, system, kind, source, signature, sha, line,
            legacy.Length == 0 ? Legacy(system) : legacy, destination, status, "", notes));
    }
    private static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string HashFile(string file)
    {
        using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    private static string Csv(string text) => "\"" + text.Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"") + "\"";

    private static void Scan(string full, string path, Scope scope)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        bool restricted = extension is ".mdf" or ".ldf" or ".bak" or ".dat";
        string sha = restricted ? "" : HashFile(full);
        bool original = scope.Role == "ORIGINAL";
        bool current = scope.Role == "CURRENT" && IsCurrentProductSource(path);
        bool supplementary = scope.Role is "SUPPLEMENTARY_DESKTOP" or "OUT_OF_SCOPE";
        string status = restricted ? "BLOCKED" : supplementary ? "REFERENCE_ONLY" : current ? "TODO" : original ? "TODO" : "REFERENCE_ONLY";
        string kind = extension switch
        {
            ".cs" => "CS_FILE", ".cshtml" => "VIEW", ".aspx" or ".ascx" or ".rdlc" or ".rpt" or ".frx" => "REPORT",
            ".config" => "CONFIG", ".sql" => "SQL_FILE", ".csproj" or ".sln" => "PROJECT",
            ".dll" or ".exe" => "BINARY", ".mdf" or ".ldf" or ".bak" or ".dat" => "DATA_FILE",
            ".css" or ".js" or ".svg" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".woff" or ".woff2" or ".ttf" or ".eot" or ".otf" => "ASSET",
            _ => "SUPPORT_FILE"
        };
        if (scope.Role == "CURRENT" && path.Contains("/OriginalDecompiled/", StringComparison.Ordinal))
            status = "REFERENCE_ONLY";
        if (scope.Role == "OUT_OF_SCOPE") status = "OUT_OF_SCOPE";
        if (kind == "BINARY") status = scope.Role == "OUT_OF_SCOPE" ? "OUT_OF_SCOPE" : "REFERENCE_ONLY";
        string note = restricted ? "Not opened/hashed; may contain real data; no production fixture"
            : $"{scope.Role}; size={new FileInfo(full).Length}; inventory does not prove behaviour";
        Add(scope.System, kind, path, path, sha, "", status, note);
        if (restricted) return;
        if (extension == ".cs" && scope.Role == "CURRENT")
            ReadCSharp(full, path, scope.System, sha, current ? "TODO" : "REFERENCE_ONLY");
        else if (extension == ".cshtml" || extension == ".aspx") ReadView(full, path, scope.System, sha, status);
        else if (extension == ".csproj") ReadProject(full, path, scope.System, sha, status);
        else if (extension == ".config") ReadConfiguration(full, path, scope.System, sha, status);
        else if (extension == ".sql") ReadSql(full, path, scope.System, sha, status);
        else if (kind == "BINARY") ReadBinaryMetadata(full, path, scope.System, sha, status);
    }

    private static void ReadCSharp(string full, string path, string system, string sha, string status)
    {
        string source = File.ReadAllText(full);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp7_3), path: path);
        var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            if (status == "TODO") _currentParseErrors++;
            Add(system, "PARSE_GAP", path, $"{diagnostics.Length} Roslyn parse errors", sha, "",
                "BLOCKED", string.Join("; ", diagnostics.Take(5).Select(x => x.Id)));
        }
        var root = tree.GetRoot();
        var sqlSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            string? kind = null, signature = null, note = "Roslyn C# 7.3 declaration; body behaviour unverified";
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    kind = "TYPE"; signature = FullName(type, type.Identifier.Text); break;
                case DelegateDeclarationSyntax del:
                    kind = "DELEGATE"; signature = FullName(del, del.Identifier.Text) + Parameters(del.ParameterList); break;
                case MethodDeclarationSyntax method:
                    kind = "METHOD";
                    signature = FullName(method, method.Identifier.Text) + Parameters(method.ParameterList);
                    if (method.TypeParameterList != null) signature += method.TypeParameterList.ToString();
                    break;
                case ConstructorDeclarationSyntax ctor:
                    kind = "CTOR"; signature = FullName(ctor, ctor.Identifier.Text) + Parameters(ctor.ParameterList); break;
                case DestructorDeclarationSyntax dtor:
                    kind = "DTOR"; signature = FullName(dtor, "~" + dtor.Identifier.Text) + Parameters(dtor.ParameterList); break;
                case PropertyDeclarationSyntax property:
                    kind = "PROPERTY"; signature = FullName(property, property.Identifier.Text); break;
                case IndexerDeclarationSyntax indexer:
                    kind = "INDEXER"; signature = FullName(indexer, "this") + Parameters(indexer.ParameterList); break;
                case EventDeclarationSyntax evt:
                    kind = "EVENT"; signature = FullName(evt, evt.Identifier.Text); break;
                case EventFieldDeclarationSyntax events:
                    foreach (var variable in events.Declaration.Variables)
                        Add(system, "EVENT", path, FullName(events, variable.Identifier.Text), sha,
                            Line(tree, variable), status, note);
                    break;
                case FieldDeclarationSyntax fields:
                    foreach (var variable in fields.Declaration.Variables)
                        Add(system, "FIELD", path, FullName(fields, variable.Identifier.Text), sha,
                            Line(tree, variable), status, note);
                    break;
                case EnumMemberDeclarationSyntax member:
                    kind = "ENUM_MEMBER"; signature = FullName(member, member.Identifier.Text); break;
                case OperatorDeclarationSyntax op:
                    kind = "OPERATOR"; signature = FullName(op, "operator " + op.OperatorToken.Text) + Parameters(op.ParameterList); break;
                case ConversionOperatorDeclarationSyntax op:
                    kind = "OPERATOR"; signature = FullName(op, op.ImplicitOrExplicitKeyword.Text + " operator " + op.Type) + Parameters(op.ParameterList); break;
                case InvocationExpressionSyntax invocation when invocation.Expression.ToString() == "RunTest":
                    if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax testLiteral)
                    {
                        string testName = testLiteral.Token.ValueText;
                        if (Regex.IsMatch(testName, @"^(TEST-\d+|T\d+):?", RegexOptions.CultureInvariant))
                            Add(system, "TEST_CASE", path, testName, sha, Line(tree, invocation), status,
                                "Registered executable test; pass/fail tracked separately");
                    }
                    break;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    if (SqlWords.IsMatch(literal.Token.ValueText))
                    {
                        string textHash = HashText(literal.Token.ValueText)[..16];
                        sqlSeen.TryGetValue(textHash, out int occurrence);
                        sqlSeen[textHash] = occurrence + 1;
                        // An occurrence count for the same fragment is stable when
                        // unrelated whitespace or source lines change. Never export SQL.
                        Add(system, "SQL_LITERAL_CANDIDATE", path,
                            $"literal:{textHash}#{occurrence}", sha, Line(tree, literal), status,
                            "Literal includes a SQL keyword; dynamic fragments not runnable without review");
                    }
                    break;
            }
            if (kind == null || signature == null) continue;
            Add(system, kind, path, signature, sha, Line(tree, node), status, note);
            if (node is MethodDeclarationSyntax action && IsActionCandidate(action))
            {
                var attrs = action.AttributeLists.SelectMany(x => x.Attributes).Select(x => x.Name.ToString()).ToArray();
                Add(system, "ACTION_CANDIDATE", path, signature, sha, Line(tree, node), status,
                    "public controller method; verb/route/filter attributes=" + string.Join("|", attrs));
            }
        }
    }
    private static string Line(SyntaxTree tree, SyntaxNode node) =>
        (tree.GetLineSpan(node.Span).StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture);
    private static string Parameters(BaseParameterListSyntax parameters) =>
        "(" + string.Join(",", parameters.Parameters.Select(p =>
            p.Modifiers.ToString().Trim() + ":" + p.Type?.ToString().Replace(" ", ""))) + ")";
    private static string FullName(SyntaxNode node, string name)
    {
        var namespaces = node.Ancestors().OfType<NamespaceDeclarationSyntax>().Reverse()
            .Select(x => x.Name.ToString());
        var types = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse()
            .Select(x => x.Identifier.Text);
        return string.Join(".", namespaces.Concat(types).Append(name));
    }
    private static bool IsActionCandidate(MethodDeclarationSyntax method)
    {
        if (method.Parent is not ClassDeclarationSyntax c || !c.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)) return false;
        if (!method.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)) || method.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))) return false;
        return !method.AttributeLists.SelectMany(x => x.Attributes)
            .Any(x => x.Name.ToString().EndsWith("NonAction", StringComparison.Ordinal) ||
                      x.Name.ToString().EndsWith("NonActionAttribute", StringComparison.Ordinal));
    }

    private static string LineAt(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index; i++) if (text[i] == '\n') line++;
        return line.ToString(CultureInfo.InvariantCulture);
    }
    private static void ReadView(string full, string path, string system, string sha, string status)
    {
        string text = File.ReadAllText(full);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in ViewLinks.Matches(text))
        {
            string target = match.Groups[2].Value + "." + match.Groups[1].Value;
            seen.TryGetValue(target, out int occurrence);
            seen[target] = occurrence + 1;
            string line = LineAt(text, match.Index);
            Add(system, "VIEW_LINK", path, $"{target}#{occurrence}", sha, line, status,
                "Razor static Url.Action link; dynamic links and generated HTML need HTTP tests");
        }
    }
    private static void ReadProject(string full, string path, string system, string sha, string status)
    {
        try
        {
            var xml = XDocument.Load(full);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in xml.Descendants().Where(x =>
                         x.Name.LocalName is "Reference" or "ProjectReference" or "PackageReference"))
            {
                string target = item.Attribute("Include")?.Value ?? "";
                if (target.Length == 0) continue;
                string kind = item.Name.LocalName;
                string key = kind + ":" + target;
                seen.TryGetValue(key, out int occurrence);
                seen[key] = occurrence + 1;
                string version = item.Attribute("Version")?.Value ?? "";
                string hint = item.Elements().Any(x => x.Name.LocalName == "HintPath") ? "HintPath supplied" : "no HintPath";
                Add(system, "PROJECT_DEPENDENCY", path, key + "#" + occurrence, sha, "", status,
                    $"{version}; {hint}; exact local resolution/compatibility must be proved");
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            Add(system, "PARSE_GAP", path, "invalid project XML", sha, "", "BLOCKED", ex.GetType().Name);
        }
    }
    private static void ReadConfiguration(string full, string path, string system, string sha, string status)
    {
        try
        {
            var xml = XDocument.Load(full);
            foreach (var item in xml.Descendants().Where(x => x.Name.LocalName is "appSettings" or "connectionStrings")
                         .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "add")))
            {
                string key = item.Attribute("key")?.Value ?? item.Attribute("name")?.Value ?? "";
                if (key.Length == 0) continue;
                Add(system, "CONFIG_KEY", path, item.Parent!.Name.LocalName + ":" + key, sha, "", status,
                    "Only key/name; NEVER export a configured value or connection string");
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            Add(system, "PARSE_GAP", path, "invalid configuration XML", sha, "", "BLOCKED", ex.GetType().Name);
        }
    }
    private static void ReadSql(string full, string path, string system, string sha, string status)
    {
        string text = File.ReadAllText(full);
        int count = 0;
        foreach (Match match in SqlLine.Matches(text))
        {
            string line = LineAt(text, match.Index);
            Add(system, "SQL_COMMAND_CANDIDATE", path, match.Groups[1].Value.ToUpperInvariant() + "#" + count++,
                sha, line, status, "Line scanner; not a parsed executable SQL statement");
        }
    }
    private static void ReadBinaryMetadata(string full, string path, string system, string sha, string status)
    {
        try
        {
            using var file = File.OpenRead(full);
            using var pe = new PEReader(file);
            if (!pe.HasMetadata) return; // native/dependent binaries remain BINARY rows
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) return;
            var assembly = reader.GetAssemblyDefinition();
            string assemblyName = reader.GetString(assembly.Name);
            string arch = pe.PEHeaders.CoffHeader.Machine + "/" + pe.PEHeaders.CorHeader?.Flags;
            Add(system, "ASSEMBLY", path, assemblyName + " " + assembly.Version, sha, "", status,
                $"PE metadata only; machine/CLR flags={arch}; SHA-256 pinned in BINARY row");
            bool originalWeb = path is "TWF 4.28/App/bin/MyTime3.dll" or "TWF 4.28/App/bin/TD5.dll"
                or "T5Web [3.5.7]/App/bin/T5V3Web.dll";
            bool desktop = path is "T5V3.9.2/T5V3.exe" or "T5V3.9.2/T5V3.UI.DLL";
            if (!originalWeb && !desktop) return;
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                string typeName = reader.GetString(type.Name);
                string nameSpace = reader.GetString(type.Namespace);
                if (originalWeb && !typeName.EndsWith("Controller", StringComparison.Ordinal)) continue;
                if (desktop && !typeName.EndsWith("Form", StringComparison.OrdinalIgnoreCase)) continue;
                string typeNameFull = nameSpace + "." + typeName;
                Add(system, desktop ? "DESKTOP_FORM" : "LEGACY_CONTROLLER", path,
                    typeNameFull, sha, "", desktop ? "REFERENCE_ONLY" : "TODO",
                    "Binary metadata, not decompiled source; no functional equivalence inferred");
                if (!originalWeb) continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public ||
                        (method.Attributes & MethodAttributes.SpecialName) != 0) continue;
                    var methodName = reader.GetString(method.Name);
                    string token = MetadataTokens.GetToken(methodHandle).ToString("X8", CultureInfo.InvariantCulture);
                    Add(system, "LEGACY_METHOD_CANDIDATE", path, $"{typeNameFull}.{methodName}@{token}", sha,
                        "", "TODO", "Public method from original binary; may not be an HTTP action; map/deny explicitly");
                }
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
        {
            Add(system, "BINARY_GAP", path, "metadata unavailable", sha, "", "BLOCKED", ex.GetType().Name);
        }
    }
    private static void ReadLegacyActionDecisions()
    {
        string path = "src/T5Web/docs/legacy-actions.csv";
        string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        string sha = HashFile(full);
        int row = 0;
        foreach (var line in File.ReadLines(full).Skip(1))
        {
            row++;
            var parts = ParseCsvLine(line);
            if (parts.Count != 5) throw new InvalidDataException($"Expected 5 fields in {path} row {row + 1}");
            string signature = parts[0] + "." + parts[1];
            Add("ARIA_ADMIN", "LEGACY_ACTION_DECISION", path, signature, sha,
                (row + 1).ToString(CultureInfo.InvariantCulture), "TODO",
                $"prior decision={parts[2]}; target={parts[3]}; must reassess, not automatically N-A; {parts[4]}",
                "T5Web [3.5.7]/App/bin/T5V3Web.dll");
        }
        if (row != 343) throw new InvalidDataException($"Original Admin action-decision count changed: {row} != 343");
    }
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var buffer = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { buffer.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(buffer.ToString()); buffer.Clear(); }
            else buffer.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unclosed CSV quoted field");
        fields.Add(buffer.ToString());
        return fields;
    }
}
