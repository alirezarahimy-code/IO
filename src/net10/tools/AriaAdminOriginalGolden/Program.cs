using System;
using System.Globalization;
using System.Reflection;

// Original Aria Admin ONLY: temporary Windows Framework probe of T5Web [3.5.7].
// Metadata for these three static TimeSpanToString methods was checked locally:
// no .cctor; their identical IL calls only System.TimeSpan/System.String.
// No controllers are instantiated, no routes, auth, engines, devices, SQL,
// connection strings, keys, licenses or customer data are accessed.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass the SHA-pinned original T5V3Web.dll path.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Assembly original = Assembly.LoadFrom(args[0]);
        if (original.GetName().Name != "T5V3Web")
            throw new InvalidOperationException("Not the original Aria Admin web assembly.");
        string[] names = {
            "T5V3Web.Controllers.RequestsReportController",
            "T5V3Web.Controllers.DailyReportController",
            "T5V3Web.Controllers.ShowReportController"
        };
        foreach (string typeName in names)
        {
            Type type = original.GetType(typeName, true);
            MethodInfo method = type.GetMethod("TimeSpanToString", BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(TimeSpan), typeof(string) }, null);
            if (method == null || method.ReturnType != typeof(string))
                throw new InvalidOperationException(typeName + " original static method/signature missing.");
            Check(method, new TimeSpan(8, 5, 0), @"hhh\:mm", "8:5", "short special format");
            Check(method, new TimeSpan(145, 30, 0), @"hhh\:mm", "145:30", "cumulative special format");
            Check(method, TimeSpan.Zero, @"hhh\:mm", "", "zero special format");
            Check(method, new TimeSpan(8, 5, 0), @"hh\:mm", "08:05", "standard format");
        }
        Console.WriteLine("Summary: 12 Passed, 0 Failed (3 original T5Web [3.5.7] static methods x 4 synthetic inputs; no net10 comparison yet)");
        return 0;
    }

    private static void Check(MethodInfo method, TimeSpan value, string format, string expected, string label)
    {
        string actual = (string)method.Invoke(null, new object[] { value, format });
        if (!String.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(method.DeclaringType.FullName + " " + label +
                ": expected '" + expected + "', got '" + actual + "'");
        Console.WriteLine("[ORIGINAL-ADMIN-PASS] " + method.DeclaringType.Name + " " + label +
            " = " + (actual.Length == 0 ? "<empty>" : actual));
    }
}
