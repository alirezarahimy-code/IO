// ORIGINAL TWF 4.28-packaged x86 attendance TimeInterval.HaveIntersection.
// Pure fixed synthetic dates only. No SQL, worker/hardware, original Web.config,
// credentials, live attendance rows, or T5Web/desktop comparison.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class OriginalAttendanceInterval
{
    private const string Expected = "2a2abd09745ec0a80dff0343b0e1f97e1ed52b8806f2ec61e64da1aa8100d3b7";

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
            string original = Path.Combine(args[0], "T5V3.Attendance.dll");
            using (var file = File.OpenRead(original))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != Expected)
                    throw new InvalidOperationException("Original Aria attendance assembly hash changed");
            // Only this pinned TWF-package DLL is directly invoked; referenced
            // assemblies, if needed, load from its original read-only directory.
            Assembly dll = Assembly.LoadFrom(original);
            Type interval = dll.GetType("T5V3.Attendance.TimeInterval", true);
            ConstructorInfo constructor = interval.GetConstructor(new[] { typeof(DateTime), typeof(DateTime) });
            MethodInfo intersects = interval.GetMethod("HaveIntersection", BindingFlags.Public | BindingFlags.Static,
                null, new[] { interval, interval }, null);
            if (constructor == null || intersects == null || intersects.ReturnType != typeof(bool))
                throw new InvalidOperationException("Original interval method signature changed");
            DateTime d = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Unspecified);
            DateTime next = d.AddDays(1);
            var samples = new[]
            {
                new Sample("overlap", d.AddHours(9), d.AddHours(10), d.AddHours(9.5), d.AddHours(10.5)),
                new Sample("disjoint", d.AddHours(9), d.AddHours(10), d.AddHours(11), d.AddHours(12)),
                new Sample("touch", d.AddHours(9), d.AddHours(10), d.AddHours(10), d.AddHours(11)),
                new Sample("same", d.AddHours(9), d.AddHours(10), d.AddHours(9), d.AddHours(10)),
                new Sample("contained", d.AddHours(9), d.AddHours(12), d.AddHours(10), d.AddHours(11)),
                new Sample("zero_inside", d.AddHours(9), d.AddHours(12), d.AddHours(10), d.AddHours(10)),
                new Sample("zero_touch", d.AddHours(9), d.AddHours(10), d.AddHours(10), d.AddHours(10)),
                new Sample("overnight", d.AddHours(23.5), next.AddHours(0.5),
                    next.AddHours(0.25), next.AddHours(1)),
            };
            var results = new List<string>();
            foreach (Sample sample in samples)
            {
                object first = constructor.Invoke(new object[] { sample.A, sample.B });
                object second = constructor.Invoke(new object[] { sample.C, sample.D });
                bool value = (bool)intersects.Invoke(null, new object[] { first, second });
                results.Add("\"" + sample.Name + "\":" + (value ? "true" : "false"));
                Console.WriteLine("::notice::Original Aria attendance overlap " + sample.Name + "=" + value);
                if ((sample.Name == "overlap" || sample.Name == "same" ||
                     sample.Name == "contained" || sample.Name == "overnight") && !value)
                    throw new InvalidOperationException("Original positive interval case changed");
                if (sample.Name == "disjoint" && value)
                    throw new InvalidOperationException("Original disjoint interval case changed");
            }
            string json = "{\"source_sha256\":\"" + Expected + "\",\"scope\":\"SYNTHETIC_PURE_ORIGINAL_X86\",\"cases\":{" +
                string.Join(",", results.ToArray()) + "}}\n";
            File.WriteAllText(args[1], json);
            Console.WriteLine("PASS: eight pure attendance interval results from pinned TWF original, no SQL/hardware");
            return 0;
        }
        catch (Exception error)
        {
            Exception cause = error;
            while (cause is TargetInvocationException && cause.InnerException != null)
                cause = cause.InnerException;
            Console.Error.WriteLine("::error title=Original attendance interval::FAIL type " +
                cause.GetType().FullName + " (no user/config details emitted)");
            return 1;
        }
    }

    private sealed class Sample
    {
        public readonly string Name;
        public readonly DateTime A, B, C, D;
        public Sample(string name, DateTime a, DateTime b, DateTime c, DateTime d)
        { Name = name; A = a; B = b; C = c; D = d; }
    }
}
