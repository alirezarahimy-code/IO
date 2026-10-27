// ORIGINAL TWF 4.28-packaged T5 permission method, not Admin/desktop parity.
// .NET Framework x86 + CI-owned synthetic LocalDB only; no config or real users.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class OriginalAriaRoleGolden
{
    private static string bin;
    private static string stage = "initialization";
    private static readonly int[] Types = { -1, 0, 1, 2, 3, 4, 5, 10, 100 };
    private static readonly int[] Levels = { -1, 0, 1, 2, 3, 4, 5, 10, 100 };

    private static void Verify(string file, string expected)
    {
        using (var input = File.OpenRead(Path.Combine(bin, file)))
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != expected)
                throw new InvalidOperationException("Original permission dependency checksum changed: " + file);
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name != "T5V3.DB" && name != "T5V3.Model" &&
            name != "EntityFramework" && name != "EntityFramework.SqlServer") return null;
        return Assembly.LoadFrom(Path.Combine(bin, name + ".dll"));
    }

    private static bool IsManager(object dao, MethodInfo method, long badge)
    {
        return (bool)method.Invoke(dao, new object[] { new List<long> { badge } });
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || IntPtr.Size != 4 || Environment.Version.Major != 4 ||
                !Regex.IsMatch(args[1], @"\AAriaGolden_[0-9]{6,20}\z") ||
                !Regex.IsMatch(args[2], @"\AAriaGoldenT5_[0-9]{6,20}\z"))
                throw new InvalidOperationException("Require Framework x86 and CI-owned LocalDB T5 names");
            bin = Path.GetFullPath(args[0]);
            Verify("T5V3.DB.dll", "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf");
            Verify("T5V3.Model.dll", "df6ccd54ba55bd7af98a9612259edba0235dbf552faa24a661e7b8012f52d9ab");
            Verify("EntityFramework.dll", "7e60999a5741b9b041d3a8d9bad1c952e4cce8216142327ab413b1ddca70a4c5");
            Verify("EntityFramework.SqlServer.dll", "4583f9d1e62c90e3bc41d9feacca8152e3bb067b767e806872772ea9a55803e9");
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            stage = "loading original Aria-packaged T5 permissions";
            Assembly assembly = Assembly.LoadFrom(Path.Combine(bin, "T5V3.DB.dll"));
            foreach (string suffix in new[] { "csdl", "ssdl", "msl" })
                if (!assembly.GetManifestResourceNames().Any(name =>
                    name.EndsWith("EntityClasses.mdlT5V3." + suffix, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Original T5 EF metadata unavailable");

            string provider = "Data Source=(localdb)\\" + args[1] + ";Initial Catalog=" + args[2] +
                              ";Integrated Security=True;Connect Timeout=10;MultipleActiveResultSets=True";
            string connection = "metadata=res://*/EntityClasses.mdlT5V3.csdl|res://*/EntityClasses.mdlT5V3.ssdl|" +
                                "res://*/EntityClasses.mdlT5V3.msl;provider=System.Data.SqlClient;" +
                                "provider connection string=\"" + provider + "\"";
            stage = "constructing original manager with private T5 connection";
            Type managerType = assembly.GetType("T5V3.DB.T5V3DBManager", true);
            ConstructorInfo ctor = managerType.GetConstructor(new[] { typeof(string) });
            if (ctor == null) throw new InvalidOperationException("Original T5V3DBManager(string) absent");
            object manager = ctor.Invoke(new object[] { connection });
            try
            {
                stage = "locating original UserPermissionDA.IsAnyMainManager";
                PropertyInfo property = managerType.GetProperty("UserPermissionDA");
                if (property == null) throw new InvalidOperationException("Original permission DAO property absent");
                object dao = property.GetValue(manager, null);
                MethodInfo check = dao.GetType().GetMethod("IsAnyMainManager", new[] { typeof(List<long>) });
                if (check == null || check.ReturnType != typeof(bool))
                    throw new InvalidOperationException("Original main-manager signature changed");
                stage = "original T5 main manager query on isolated synthetic permission table";
                if (IsManager(dao, check, 999999))
                    throw new InvalidOperationException("Unassigned synthetic badge acquired manager status");
                var matches = new List<string>();
                int i = 0;
                foreach (int type in Types)
                    foreach (int level in Levels)
                    {
                        if (IsManager(dao, check, 910000 + i))
                            matches.Add(type + "/" + level);
                        ++i;
                    }
                Console.WriteLine("::notice::Original Aria-packaged IsAnyMainManager synthetic matched type/level pairs=" +
                                  (matches.Count == 0 ? "none" : string.Join(",", matches)) +
                                  "; cases=" + i);
                if (matches.Count != 1 || matches[0] != "0/1")
                    throw new InvalidOperationException("Original TWF Aria manager permission matrix drift");
                stage = "original manager multi-badge and empty decisions";
                bool mixed = (bool)check.Invoke(dao, new object[] { new List<long> { 910000, 910011 } });
                bool regular = (bool)check.Invoke(dao, new object[] { new List<long> { 910000, 999999 } });
                bool empty = (bool)check.Invoke(dao, new object[] { new List<long>() });
                Console.WriteLine("::notice::Original manager list semantics: mixedManager=" + mixed +
                                  "; regularDenied=" + !regular + "; emptyDenied=" + !empty);
                if (!mixed || regular || empty)
                    throw new InvalidOperationException("Original manager badge-list golden drift");
                Console.WriteLine("PASS: 81 original Aria role pairs + 4 badge-list outcomes; LocalDB ONLY, no SQL2016/net10 role");
                return 0;
            }
            finally
            {
                IDisposable disposable = manager as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }
        catch (Exception error)
        {
            Exception cause = error;
            while (cause is TargetInvocationException && cause.InnerException != null)
                cause = cause.InnerException;
            Console.Error.WriteLine("::error title=Original Aria manager fixture::FAIL at " + stage +
                                    "; exception type: " + cause.GetType().FullName);
            if (cause.GetType().FullName == "System.Data.SqlClient.SqlException")
                Console.Error.WriteLine("::error::SQL error number: " +
                                        cause.GetType().GetProperty("Number").GetValue(cause, null));
            return 1;
        }
    }
}
