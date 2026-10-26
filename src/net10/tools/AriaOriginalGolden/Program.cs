using System;
using System.Globalization;
using System.IO;
using System.Reflection;

// Diagnostic Windows/.NET Framework-only runner for *original* TWF 4.28/App/bin/MyTime3.dll.
// Invokes ONLY two pure static MyTime3.Controllers.Misc methods with fixed synthetic
// inputs. No application startup, lock check, SQL, password, token or hardware.
// This is not part of a new product or a replacement for the 47 TWF rewrite tests.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Pass the immutable original MyTime3.dll path and a temporary golden file path.");
        Assembly original = Assembly.LoadFrom(args[0]);
        if (original.GetName().Name != "MyTime3")
            throw new InvalidOperationException("Loaded assembly is not the original Aria web assembly.");
        Type misc = original.GetType("MyTime3.Controllers.Misc", true);
        MethodInfo format = misc.GetMethod("TimeSpanToString", new Type[] { typeof(TimeSpan) });
        MethodInfo parse = misc.GetMethod("StringToTimeSpan", new Type[] { typeof(string) });
        if (format == null || parse == null || !format.IsStatic || !parse.IsStatic)
            throw new InvalidOperationException("Pure original Misc entry points are absent.");

        // These assertions are diagnostic expectations from decompiled TWF 4.28,
        // not adopted as a new product's UI semantics until explicit mapping.
        string shortTime = CheckString(format, new TimeSpan(8, 5, 0), "8:5", "AR-ORIG-01 short time (no padding)");
        string cumulative = CheckString(format, new TimeSpan(145, 30, 0), "145:30", "AR-ORIG-02 cumulative time");
        string zero = CheckString(format, TimeSpan.Zero, "0:0", "AR-ORIG-03 zero time");
        TimeSpan parsed = (TimeSpan)parse.Invoke(null, new object[] { "8:05" });
        if (parsed != new TimeSpan(8, 5, 0))
            throw new InvalidOperationException("Original StringToTimeSpan 8:05 differs from the decompiled reference.");
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-04 parse 8:05 = " + parsed.ToString("c", CultureInfo.InvariantCulture));
        // Only the three format results are being ported. The parse result is
        // evidence about the original, not an excuse to expose its lenient parser.
        File.WriteAllLines(args[1], new string[] {
            "source_sha256=edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b",
            "format_8h5m=" + shortTime,
            "format_145h30m=" + cumulative,
            "format_zero=" + zero
        });
        Console.WriteLine("Summary: 4 Passed, 0 Failed (original TWF 4.28 MyTime3.dll, pure synthetic methods only)");
        return 0;
    }

    private static string CheckString(MethodInfo method, TimeSpan input, string expected, string name)
    {
        string actual = (string)method.Invoke(null, new object[] { input });
        if (!String.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(name + ": expected '" + expected + "', actual '" + actual + "'");
        Console.WriteLine("[ORIGINAL-PASS] " + name + " = " + actual);
        return actual;
    }
}
