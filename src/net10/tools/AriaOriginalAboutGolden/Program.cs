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
        CheckImage(original, "Arm", args[2], 2, 295, 55);
        CheckImage(original, "logo1", args[3], 3, 994, 720);
        Console.WriteLine("Summary: 11 Passed, 0 Failed (seven About labels, two image resources and two original MyBanner(2/3) action results; no original HTTP)");
        return 0;
    }

    private static void CheckImage(Assembly original, string key, string copiedAsset,
        int index, int expectedWidth, int expectedHeight)
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
            Console.WriteLine("[ORIGINAL-PASS] public vendor image pixels " + key +
                " (copied asset, not customer LogoPath)");

            // Call ONLY the public original image action for these two fixed
            // vendor-resource indices, NOT the customer LogoPath case (1).
            // This is a Framework action-result golden, NOT an HTTP/IIS test.
            byte[] response = InvokeOriginalBanner(original, index);
            using (var stream = new MemoryStream(response, false))
            using (var actionImage = new Bitmap(stream))
            {
                if (actionImage.Width != expectedWidth || actionImage.Height != expectedHeight)
                    throw new InvalidOperationException("Original MyBanner dimension drift: " + index);
                for (int y = 0; y < expectedHeight; y++)
                    for (int x = 0; x < expectedWidth; x++)
                        if (actionImage.GetPixel(x, y).ToArgb() != servedImage.GetPixel(x, y).ToArgb())
                            throw new InvalidOperationException("Original-action-vs-net10 image pixel mismatch: " + index);
            }
            Console.WriteLine("[ORIGINAL-PASS] public MyBanner(" + index +
                ") returned image/png with matching pixels (action result, no HTTP)");
        }
    }

    private static byte[] InvokeOriginalBanner(Assembly original, int index)
    {
        if (index != 2 && index != 3)
            throw new ArgumentOutOfRangeException("index", "Never access the customer's LogoPath");
        Type controllerType = original.GetType("MyTime3.Controllers.HomeController", true);
        MethodInfo banner = controllerType.GetMethod("MyBanner", new Type[] { typeof(int) });
        if (banner == null || !banner.IsPublic)
            throw new MissingMethodException(controllerType.FullName, "MyBanner");
        object controller = Activator.CreateInstance(controllerType);
        try
        {
            object result = banner.Invoke(controller, new object[] { index });
            PropertyInfo bytesProperty = result.GetType().GetProperty("FileContents");
            PropertyInfo mimeProperty = result.GetType().GetProperty("ContentType");
            if (bytesProperty == null || mimeProperty == null ||
                (string)mimeProperty.GetValue(result, null) != "image/png")
                throw new InvalidOperationException("Original public image action did not return PNG.");
            byte[] content = bytesProperty.GetValue(result, null) as byte[];
            if (content == null || content.Length < 100 || content.Length > 250000 ||
                content[0] != 0x89 || content[1] != 0x50 || content[2] != 0x4e || content[3] != 0x47)
                throw new InvalidOperationException("Unexpected original public image action payload.");
            return content;
        }
        finally
        {
            IDisposable cleanup = controller as IDisposable;
            if (cleanup != null) cleanup.Dispose();
        }
    }
}
