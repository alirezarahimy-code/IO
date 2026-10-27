// Read ONLY the pinned TWF 4.28-packaged T5V3.DB.dll embedded EDMX SSDL.
// No connection, SQL command, original Web.config, real row or admin binary.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class OriginalAriaRequestSchema
{
    private const string Expected = "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf";
    private const string Resource = "EntityClasses.mdlT5V3.ssdl";
    private static readonly KeyValuePair<string, string>[] Fields = {
        new KeyValuePair<string, string>("RequestID", "bigint"),
        new KeyValuePair<string, string>("BadgeNumber", "bigint"),
        new KeyValuePair<string, string>("WofkflowType", "int"),
        new KeyValuePair<string, string>("ForDate", "datetime"),
        new KeyValuePair<string, string>("FromTime", "datetime"),
        new KeyValuePair<string, string>("ToTime", "datetime"),
        new KeyValuePair<string, string>("IsSuspend", "bit"),
        new KeyValuePair<string, string>("IsDaily", "bit")
    };

    private static int Main(string[] args)
    {
        try
        {
            string temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            if (args.Length != 2 || IntPtr.Size != 4 || Environment.Version.Major != 4 ||
                string.IsNullOrEmpty(temp) ||
                !Path.GetFullPath(args[1]).StartsWith(Path.GetFullPath(temp).TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Require Framework x86 and runner-temp output");
            string original = Path.Combine(args[0], "T5V3.DB.dll");
            using (var input = File.OpenRead(original))
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != Expected)
                    throw new InvalidOperationException("Original Aria DB assembly SHA drift");
            Assembly assembly = Assembly.LoadFrom(original);
            using (Stream stream = assembly.GetManifestResourceStream(Resource))
            {
                if (stream == null || !stream.CanRead || stream.Length > 1000000)
                    throw new InvalidOperationException("Original T5 EDMX resource absent/oversized");
                XDocument document = XDocument.Load(stream);
                XElement root = document.Root;
                if (root == null) throw new InvalidOperationException("Empty SSDL");
                XNamespace ns = root.Name.Namespace;
                XElement container = root.Element(ns + "EntityContainer");
                XElement table = container == null ? null : container.Elements(ns + "EntitySet")
                    .SingleOrDefault(e => (string)e.Attribute("Name") == "Request");
                if (table == null || (string)table.Attribute("Schema") != "dbo" ||
                    (string)table.Attribute("EntityType") != "Self.Request" ||
                    (table.Attribute("Table") != null && (string)table.Attribute("Table") != "Request"))
                    throw new InvalidOperationException("Original dbo.Request set mapping changed");
                XElement entity = root.Elements(ns + "EntityType")
                    .SingleOrDefault(e => (string)e.Attribute("Name") == "Request");
                if (entity == null) throw new InvalidOperationException("Request entity missing");
                var properties = entity.Elements(ns + "Property").ToDictionary(e => (string)e.Attribute("Name"));
                foreach (var field in Fields)
                {
                    XElement property;
                    if (!properties.TryGetValue(field.Key, out property) ||
                        (string)property.Attribute("Type") != field.Value ||
                        (string)property.Attribute("Nullable") != "false")
                        throw new InvalidOperationException("Original Request selected column type/nullability changed");
                }
            }
            // Output only the fixed selected *schema* contract. No values,
            // geographic/photo/description/request row fields leave the runner.
            const string schema = "{\"source_sha256\":\"" + Expected +
                "\",\"scope\":\"ORIGINAL_TWF_REQUEST_EDMX_ONLY_X86\",\"table\":\"dbo.Request\",\"columns\":{\"RequestID\":\"bigint\",\"BadgeNumber\":\"bigint\",\"WofkflowType\":\"int\",\"ForDate\":\"datetime\",\"FromTime\":\"datetime\",\"ToTime\":\"datetime\",\"IsSuspend\":\"bit\",\"IsDaily\":\"bit\"}}\n";
            File.WriteAllText(args[1], schema);
            Console.WriteLine("PASS: pinned Aria T5 request table and 8 selected non-sensitive EDMX columns; schema only, no SQL/rows");
            return 0;
        }
        catch (Exception error)
        {
            Exception cause = error is TargetInvocationException && error.InnerException != null
                ? error.InnerException : error;
            Console.Error.WriteLine("::error title=Original Aria Request schema::FAIL type " +
                cause.GetType().FullName + " (no configuration/data emitted)");
            return 1;
        }
    }
}
