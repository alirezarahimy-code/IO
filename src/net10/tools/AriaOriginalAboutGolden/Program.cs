using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

// Read-only Windows/.NET Framework extraction of PUBLIC About labels and
// byte-independent image pixels from the SHA-pinned ORIGINAL TWF 4.28 DLL.
// Not a Razor renderer, original MyBanner HTTP action, customer LogoPath,
// Personnel connection, product version copy, or full HTTP/HTML golden.
internal static class Program
{
    private static readonly string[] Keys =
    {
        "About", "bode5", "bode5Descriptions1", "bode5Descriptions2",
        "bode5Tel", "bode5Fac", "bode5Copy"
    };

    private static int Main(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("Pass pinned original DLL, temp label fixture and two copied public asset paths.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");
        string bin = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string file = Path.Combine(bin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        Assembly original = Assembly.LoadFrom(args[0]);
        if (original.GetName().Name != "MyTime3")
            throw new InvalidOperationException("Expected original TWF 4.28 MyTime3, not a rewrite.");
        Type content = original.GetType("MyTime3.Content.PersianString", true);
        var lines = new List<string>
        {
            "source_sha256=edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b"
        };
        foreach (string key in Keys)
        {
            PropertyInfo property = content.GetProperty(key, BindingFlags.Public | BindingFlags.Static);
            if (property == null || property.PropertyType != typeof(string))
                throw new MissingMemberException(content.FullName, key);
            string value = property.GetValue(null, null) as string;
            if (string.IsNullOrEmpty(value) || value.Length > 300 ||
                value.IndexOf('<') >= 0 || value.IndexOf('>') >= 0)
                throw new InvalidOperationException("Original About label missing, unsafe or unexpectedly long: " + key);
            lines.Add(key + "=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
            // Never print extracted strings: the only output is a bounded key
            // list; runner-temp values are compared by the separate .NET 10 Web.
            Console.WriteLine("[ORIGINAL-PASS] public About resource " + key);
        }
        File.WriteAllLines(args[1], lines.ToArray(), new UTF8Encoding(false));
        CheckImage(original, "Arm", args[2], 295, 55);
        CheckImage(original, "logo1", args[3], 994, 720);
        Console.WriteLine("Summary: 9 Passed, 0 Failed (seven original About labels, two vendor image pixel sets; no original HTTP)");
        return 0;
    }

    private static void CheckImage(Assembly original, string key, string copiedAsset,
        int expectedWidth, int expectedHeight)
    {
        Type images = original.GetType("MyTime3.Content.ImagesResource", true);
        PropertyInfo property = images.GetProperty(key, BindingFlags.Public | BindingFlags.Static);
        if (property == null || property.PropertyType != typeof(Bitmap))
            throw new MissingMemberException(images.FullName, key);
        using (Bitmap originalImage = (Bitmap)property.GetValue(null, null))
        using (Bitmap servedImage = new Bitmap(copiedAsset))
        {
            if (originalImage == null || originalImage.Width != expectedWidth ||
                originalImage.Height != expectedHeight ||
                servedImage.Width != expectedWidth || servedImage.Height != expectedHeight)
                throw new InvalidOperationException("Original public vendor image dimension drift: " + key);
            for (int y = 0; y < expectedHeight; y++)
                for (int x = 0; x < expectedWidth; x++)
                    if (originalImage.GetPixel(x, y).ToArgb() != servedImage.GetPixel(x, y).ToArgb())
                        throw new InvalidOperationException("Original-vs-offline image pixel mismatch: " + key);
        }
        Console.WriteLine("[ORIGINAL-PASS] public vendor image pixels " + key +
            " (copied asset, not customer LogoPath)");
    }
}
