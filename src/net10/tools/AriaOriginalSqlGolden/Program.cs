// .NET Framework x86 ONLY. Execute the ORIGINAL pinned TWF 4.28 CheckUserPass
// against a disposable CI-created LocalDB fixture; never load original config.
// LocalDB is NOT SQL Server 2016 parity or an operational net10 login.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class OriginalSqlGolden
{
    private const string PersonnelHash = "468e43ea66b9c591384ddcd033918e6f563a85b4caa2b6e6c4e8b85a4484050f";
    private const string EfHash = "7e60999a5741b9b041d3a8d9bad1c952e4cce8216142327ab413b1ddca70a4c5";
    private const string EfSqlHash = "4583f9d1e62c90e3bc41d9feacca8152e3bb067b767e806872772ea9a55803e9";
    private static string originalDirectory;
    private static string stage = "initialization";

    private static void Verify(string filename, string expected)
    {
        using (var stream = File.OpenRead(Path.Combine(originalDirectory, filename)))
        using (var hash = SHA256.Create())
        {
            string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (actual != expected)
                throw new InvalidOperationException("Original DLL checksum mismatch: " + filename);
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name != "EntityFramework" && name != "EntityFramework.SqlServer" && name != "Personnel")
            return null;
        return Assembly.LoadFrom(Path.Combine(originalDirectory, name + ".dll"));
    }

    private static long Invoke(object dao, MethodInfo method, string account, string password, bool webOnly)
    {
        return (long)method.Invoke(dao, new object[] { account, password, webOnly });
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || IntPtr.Size != 4 || Environment.Version.Major != 4 ||
                !Regex.IsMatch(args[1], @"\AAriaGolden_[0-9]{6,20}\z") ||
                !Regex.IsMatch(args[2], @"\AAriaGoldenDb_[0-9]{6,20}\z"))
                throw new InvalidOperationException("Require Framework x86 and CI-owned LocalDB instance/catalog arguments");
            originalDirectory = Path.GetFullPath(args[0]);
            Verify("Personnel.dll", PersonnelHash);
            Verify("EntityFramework.dll", EfHash);
            Verify("EntityFramework.SqlServer.dll", EfSqlHash);
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            stage = "loading pinned original";
            Assembly original = Assembly.LoadFrom(Path.Combine(originalDirectory, "Personnel.dll"));
            string[] embedded = original.GetManifestResourceNames();
            foreach (string extension in new[] { "csdl", "ssdl", "msl" })
                if (!embedded.Any(name => name.EndsWith("DB.PersonnelMdl." + extension, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Original embedded EF model is missing");

            // All connection components are constructed here from validated synthetic names.
            // No Web.config, appSettings, environment connection string, user secret or MDF is read.
            string provider = "Data Source=(localdb)\\" + args[1] + ";Initial Catalog=" + args[2] +
                              ";Integrated Security=True;Connect Timeout=10;MultipleActiveResultSets=True";
            string connection = "metadata=res://*/DB.PersonnelMdl.csdl|res://*/DB.PersonnelMdl.ssdl|" +
                                "res://*/DB.PersonnelMdl.msl;provider=System.Data.SqlClient;" +
                                "provider connection string=\"" + provider + "\"";
            stage = "constructing PersonnelSystem with isolated connection";
            Type systemType = original.GetType("Personnel.PersonnelSystem", true);
            ConstructorInfo ctor = systemType.GetConstructor(new[] { typeof(string) });
            if (ctor == null)
                throw new InvalidOperationException("Original PersonnelSystem(string) is absent");
            object system = ctor.Invoke(new object[] { connection });
            try
            {
                stage = "locating original CheckUserPass";
                PropertyInfo dbProperty = systemType.GetProperty("DB");
                if (dbProperty == null)
                    throw new InvalidOperationException("Original PersonnelSystem.DB is absent");
                object db = dbProperty.GetValue(system, null);
                PropertyInfo usersProperty = db.GetType().GetProperty("UserInfos");
                if (usersProperty == null)
                    throw new InvalidOperationException("Original DB.UserInfos is absent");
                object users = usersProperty.GetValue(db, null);
                MethodInfo check = users.GetType().GetMethod("CheckUserPass", new[] {
                    typeof(string), typeof(string), typeof(bool) });
                if (check == null || check.ReturnType != typeof(long))
                    throw new InvalidOperationException("Original CheckUserPass signature changed");

                stage = "original CheckUserPass: valid synthetic account";
                long valid = Invoke(users, check, "fixture-aria-active", "fixture-aria-password", true);
                stage = "original CheckUserPass: wrong synthetic password";
                long wrong = Invoke(users, check, "fixture-aria-active", "fixture-aria-incorrect", true);
                stage = "original CheckUserPass: missing synthetic account";
                long missing = Invoke(users, check, "fixture-aria-missing", "fixture-aria-password", true);
                stage = "original CheckUserPass: future validity";
                long future = Invoke(users, check, "fixture-aria-future", "fixture-aria-future-pass", true);
                stage = "original CheckUserPass: expired validity";
                long expired = Invoke(users, check, "fixture-aria-expired", "fixture-aria-expired-pass", true);
                // Print these five bounded outcomes BEFORE attempting the non-web
                // row: the original method may throw for that row, which must not
                // erase evidence of the first five actual runtime executions.
                Console.WriteLine("TWF 4.28 original CheckUserPass on synthetic LocalDB: " +
                    "valid=" + (valid == 900101) + " wrongDenied=" + (wrong <= 0) +
                    " absentDenied=" + (missing <= 0) + " futureDenied=" + (future <= 0) +
                    " expiredDenied=" + (expired <= 0));
                if (valid != 900101 || wrong > 0 || missing > 0 || future > 0 || expired > 0)
                    throw new InvalidOperationException("Original password golden fixture contract drift");
                stage = "original CheckUserPass: non-web synthetic account";
                bool nonWebThrows = false;
                try
                {
                    Invoke(users, check, "fixture-aria-disabled", "fixture-aria-disabled-pass", true);
                }
                catch (TargetInvocationException error)
                {
                    // The previous pinned run reached this case and the original
                    // threw InvalidOperationException; pin that observation, not a
                    // made-up clean denial. Do not print the exception message.
                    if (!(error.InnerException is InvalidOperationException)) throw;
                    nonWebThrows = true;
                }
                Console.WriteLine("Original synthetic non-web account throws InvalidOperationException=" + nonWebThrows);
                if (!nonWebThrows)
                    throw new InvalidOperationException("Original non-web exception behavior drift");
                Console.WriteLine("PASS: five original outcomes + one original exception; LocalDB ONLY, not SQL2016/net10 login");
                return 0;
            }
            finally
            {
                IDisposable disposable = system as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
        }
        catch (Exception exception)
        {
            Exception cause = exception;
            while (cause is TargetInvocationException && cause.InnerException != null)
                cause = cause.InnerException;
            // Exception messages can contain connection details: never print them or a stack trace.
            Console.Error.WriteLine("::error title=Original CheckUserPass fixture::FAIL at " + stage +
                                    "; exception type: " + cause.GetType().FullName);
            if (cause.GetType().FullName == "System.Data.SqlClient.SqlException")
                Console.Error.WriteLine("::error::SQL error number: " +
                                        cause.GetType().GetProperty("Number").GetValue(cause, null));
            return 1;
        }
    }
}
