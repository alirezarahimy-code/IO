// TWF 4.28-packaged RequestDA.GetAll ORIGINAL read-only methods on ONLY a
// disposable, CI-owned LocalDB with five fabricated rows. Framework x86.
// Never load production config/data or the independent T5Web Admin binary.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class OriginalAriaRequestQuery
{
    private static string bin;
    private static string stage = "initialization";
    private static void Verify(string file, string expected)
    {
        using (var input = File.OpenRead(Path.Combine(bin, file)))
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != expected)
                throw new InvalidOperationException("Original dependency checksum changed");
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name != "T5V3.DB" && name != "T5V3.Model" &&
            name != "EntityFramework" && name != "EntityFramework.SqlServer") return null;
        return Assembly.LoadFrom(Path.Combine(bin, name + ".dll"));
    }

    private static long[] Ids(object dao, MethodInfo method, object[] parameters)
    {
        IEnumerable rows = (IEnumerable)method.Invoke(dao, parameters);
        var ids = new List<long>();
        foreach (object row in rows)
        {
            // Never disclose any request column except fixed fixture-only IDs.
            PropertyInfo id = row.GetType().GetProperty("RequestID");
            if (id == null) throw new InvalidOperationException("Original model ID absent");
            ids.Add(Convert.ToInt64(id.GetValue(row, null)));
        }
        return ids.OrderBy(value => value).ToArray();
    }

    private static string Json(long[] values)
    {
        return "[" + string.Join(",", values.Select(v => v.ToString()).ToArray()) + "]";
    }

    private static int Main(string[] args)
    {
        try
        {
            string temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            if (args.Length != 4 || IntPtr.Size != 4 || Environment.Version.Major != 4 ||
                !Regex.IsMatch(args[1], @"\AAriaRequest_[0-9]{6,20}\z") ||
                !Regex.IsMatch(args[2], @"\AAriaRequestT5_[0-9]{6,20}\z") ||
                string.IsNullOrEmpty(temp) ||
                !Path.GetFullPath(args[3]).StartsWith(Path.GetFullPath(temp).TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Require original Framework x86 and CI-owned request fixture");
            bin = Path.GetFullPath(args[0]);
            Verify("T5V3.DB.dll", "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf");
            Verify("T5V3.Model.dll", "df6ccd54ba55bd7af98a9612259edba0235dbf552faa24a661e7b8012f52d9ab");
            Verify("EntityFramework.dll", "7e60999a5741b9b041d3a8d9bad1c952e4cce8216142327ab413b1ddca70a4c5");
            Verify("EntityFramework.SqlServer.dll", "4583f9d1e62c90e3bc41d9feacca8152e3bb067b767e806872772ea9a55803e9");
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            stage = "loading original Aria T5 request DA";
            Assembly assembly = Assembly.LoadFrom(Path.Combine(bin, "T5V3.DB.dll"));
            string provider = "Data Source=(localdb)\\" + args[1] + ";Initial Catalog=" + args[2] +
                              ";Integrated Security=True;Connect Timeout=10;MultipleActiveResultSets=True";
            string connection = "metadata=res://*/EntityClasses.mdlT5V3.csdl|res://*/EntityClasses.mdlT5V3.ssdl|" +
                                "res://*/EntityClasses.mdlT5V3.msl;provider=System.Data.SqlClient;" +
                                "provider connection string=\"" + provider + "\"";
            Type managerType = assembly.GetType("T5V3.DB.T5V3DBManager", true);
            ConstructorInfo ctor = managerType.GetConstructor(new[] { typeof(string) });
            if (ctor == null) throw new InvalidOperationException("Original T5 manager constructor changed");
            object manager = ctor.Invoke(new object[] { connection });
            try
            {
                PropertyInfo property = managerType.GetProperty("RequestDA");
                if (property == null) throw new InvalidOperationException("Original RequestDA absent");
                object dao = property.GetValue(manager, null);
                Type da = dao.GetType();
                MethodInfo getAll = da.GetMethod("GetAll", new[] { typeof(long) });
                MethodInfo getFlag = da.GetMethod("GetAll", new[] { typeof(long), typeof(bool) });
                MethodInfo getRange = da.GetMethod("GetAll", new[] { typeof(long), typeof(DateTime), typeof(DateTime) });
                if (getAll == null || getFlag == null || getRange == null)
                    throw new InvalidOperationException("Original RequestDA.GetAll overload drift");
                stage = "reading only five synthetic Request rows through original DA";
                DateTime day = new DateTime(2026, 10, 27);
                long[] all = Ids(dao, getAll, new object[] { 900101L });
                long[] empty = Ids(dao, getAll, new object[] { 900999L });
                long[] range = Ids(dao, getRange, new object[] { 900101L, day, day.AddDays(1) });
                long[] exact = Ids(dao, getRange, new object[] { 900101L, day, day });
                long[] flagFalse = Ids(dao, getFlag, new object[] { 900101L, false });
                long[] flagTrue = Ids(dao, getFlag, new object[] { 900101L, true });
                if (!all.SequenceEqual(new long[] { 810001, 810002, 810003, 810004 }) ||
                    empty.Length != 0 ||
                    range.Any(id => !all.Contains(id)) || exact.Any(id => !all.Contains(id)) ||
                    flagFalse.Any(id => !all.Contains(id)) || flagTrue.Any(id => !all.Contains(id)))
                    throw new InvalidOperationException("Original RequestDA synthetic badge boundary changed");
                string json = "{\"source_sha256\":\"d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf\",\"scope\":\"ORIGINAL_ARIA_REQUEST_LOCALDB_SYNTHETIC_ONLY\",\"all\":" + Json(all) +
                    ",\"absent\":" + Json(empty) + ",\"range_inclusive_probe\":" + Json(range) +
                    ",\"exact_midnight\":" + Json(exact) + ",\"flag_false\":" + Json(flagFalse) +
                    ",\"flag_true\":" + Json(flagTrue) + "}\n";
                File.WriteAllText(args[3], json);
                Console.WriteLine("::notice::Original Aria synthetic RequestDA.GetAll IDs: all=" + Json(all) +
                                  "; absent=" + Json(empty) + "; range=" + Json(range) +
                                  "; exact=" + Json(exact) + "; false=" + Json(flagFalse) +
                                  "; true=" + Json(flagTrue));
                Console.WriteLine("PASS: pinned original TWF RequestDA only on private LocalDB, no new SQL provider or HTTP");
                return 0;
            }
            finally
            {
                IDisposable resource = manager as IDisposable;
                if (resource != null) resource.Dispose();
            }
        }
        catch (Exception error)
        {
            Exception cause = error;
            while (cause is TargetInvocationException && cause.InnerException != null)
                cause = cause.InnerException;
            Console.Error.WriteLine("::error title=Original Aria request query::FAIL at " + stage +
                                    "; exception type " + cause.GetType().FullName);
            if (cause.GetType().FullName == "System.Data.SqlClient.SqlException")
                Console.Error.WriteLine("::error::Synthetic fixture SQL number " +
                                        cause.GetType().GetProperty("Number").GetValue(cause, null));
            return 1;
        }
    }
}
