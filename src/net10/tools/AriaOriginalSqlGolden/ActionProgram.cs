// In-process ORIGINAL TWF 4.28 LoginController.TestUserPass on synthetic SQL.
// This invokes an MVC action METHOD only; it is NOT original HTTP/IIS/HTML.
// NEVER read original Web.config; only a fresh runner-temp EXE.config is used.
using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class OriginalAriaTestUserPassAction
{
    private static string bin;
    private static string stage = "startup";

    private static void Verify(string file, string sha)
    {
        using (var input = File.OpenRead(Path.Combine(bin, file)))
        using (var hash = SHA256.Create())
            if (BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != sha)
                throw new InvalidOperationException("Original action DLL checksum mismatch: " + file);
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        // Never auto-load user data, Web.config, arbitrary DLLs or Link5.
        if (name != "Personnel" && name != "EntityFramework" &&
            name != "EntityFramework.SqlServer" && name != "MyTime3")
            return null;
        return Assembly.LoadFrom(Path.Combine(bin, name + ".dll"));
    }

    private static bool Invoke(object controller, MethodInfo action, Type modelType,
        string account, string password)
    {
        object model = Activator.CreateInstance(modelType);
        modelType.GetProperty("UserName").SetValue(model, account, null);
        modelType.GetProperty("Password").SetValue(model, password, null);
        object json = action.Invoke(controller, new[] { model });
        object data = json.GetType().GetProperty("Data").GetValue(json, null);
        PropertyInfo result = data.GetType().GetProperty("result");
        if (result == null || result.PropertyType != typeof(bool))
            throw new InvalidOperationException("Original TestUserPass JSON result shape changed");
        return (bool)result.GetValue(data, null);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || IntPtr.Size != 4 || Environment.Version.Major != 4 ||
                !Regex.IsMatch(args[1], @"\AAriaGolden_[0-9]{6,20}\z") ||
                !Regex.IsMatch(args[2], @"\AAriaGoldenDb_[0-9]{6,20}\z"))
                throw new InvalidOperationException("Only Framework x86 in CI-owned instance is allowed");
            bin = Path.GetFullPath(args[0]);
            string exeDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            if (exeDir.StartsWith(bin, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(exeDir, "AriaOriginalActionGolden.exe.config")))
                throw new InvalidOperationException("Missing OWNED disposable executable configuration");
            string ownedConnection = ConfigurationManager.AppSettings["PersonnelCnnStr"];
            if (ownedConnection == null ||
                !ownedConnection.Contains("Data Source=(localdb)\\" + args[1]) ||
                !ownedConnection.Contains("Initial Catalog=" + args[2]) ||
                !ownedConnection.Contains("Integrated Security=True"))
                throw new InvalidOperationException("Refusing configuration outside CI-owned LocalDB catalog");
            Verify("MyTime3.dll", "edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b");
            Verify("Personnel.dll", "468e43ea66b9c591384ddcd033918e6f563a85b4caa2b6e6c4e8b85a4484050f");
            Verify("EntityFramework.dll", "7e60999a5741b9b041d3a8d9bad1c952e4cce8216142327ab413b1ddca70a4c5");
            Verify("EntityFramework.SqlServer.dll", "4583f9d1e62c90e3bc41d9feacca8152e3bb067b767e806872772ea9a55803e9");
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            stage = "loading original Aria LoginController from pinned binary";
            Assembly assembly = Assembly.LoadFrom(Path.Combine(bin, "MyTime3.dll"));
            Type controllerType = assembly.GetType("MyTime3.Controllers.LoginController", true);
            Type modelType = assembly.GetType("MyTime3.Models.LoginModel", true);
            MethodInfo action = controllerType.GetMethod("TestUserPass", new[] { modelType });
            if (action == null) throw new InvalidOperationException("Original TestUserPass(LoginModel) missing");
            stage = "original TestUserPass with synthetic executable config/SQL";
            object controller = Activator.CreateInstance(controllerType);
            try
            {
                bool valid = Invoke(controller, action, modelType, "fixture-aria-active", "fixture-aria-password");
                bool wrong = Invoke(controller, action, modelType, "fixture-aria-active", "fixture-aria-incorrect");
                bool absent = Invoke(controller, action, modelType, "fixture-aria-missing", "fixture-aria-password");
                bool future = Invoke(controller, action, modelType, "fixture-aria-future", "fixture-aria-future-pass");
                bool expired = Invoke(controller, action, modelType, "fixture-aria-expired", "fixture-aria-expired-pass");
                bool nonWeb = Invoke(controller, action, modelType, "fixture-aria-disabled", "fixture-aria-disabled-pass");
                Console.WriteLine("::notice::ORIGINAL TestUserPass action METHOD JSON synthetic: valid=" + valid +
                                  "; wrong=" + wrong + "; absent=" + absent + "; future=" + future +
                                  "; expired=" + expired + "; nonWeb=" + nonWeb);
                if (!valid || wrong || absent || future || expired || !nonWeb)
                    throw new InvalidOperationException("Original action method synthetic JSON contract drift");
                Console.WriteLine("PASS: six original MVC action results on synthetic SQL; NOT actual HTTP/IIS/CSRF/net10 login");
                return 0;
            }
            finally
            {
                IDisposable disposable = controller as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }
        catch (Exception error)
        {
            Exception cause = error;
            while (cause is TargetInvocationException && cause.InnerException != null)
                cause = cause.InnerException;
            Console.Error.WriteLine("::error title=Original Aria action fixture::FAIL at " + stage +
                                    "; exception type: " + cause.GetType().FullName);
            if (cause.GetType().FullName == "System.Data.SqlClient.SqlException")
                Console.Error.WriteLine("::error::SQL error number: " +
                                        cause.GetType().GetProperty("Number").GetValue(cause, null));
            return 1;
        }
    }
}
