using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web;

// Windows/.NET Framework probe against ONLY the pinned TWF 4.28 original
// MyTime3.dll. The original creates synthetic in-memory cookies; this program
// never prints their values, reuses a real cookie, calls Login/SQL/Settings,
// starts IIS, or copies original encryption into the net10 product.
internal static class Program
{
    private static int _passed;
    private static MethodInfo _encrypt;
    private static MethodInfo _getRole;
    private static MethodInfo _getBadge;
    private static MethodInfo _isExpired;
    private static MethodInfo _authorize;
    private static Type _filter;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass the SHA-pinned TWF 4.28/App/bin/MyTime3.dll path.");
        // The probe runs on the same Windows machine as the original assembly.
        // The legacy DateTime.Parse call is culture-sensitive; pin its culture.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        string bin = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string local = Path.Combine(bin, name + ".dll");
            return File.Exists(local) ? Assembly.LoadFrom(local) : null;
        };
        Assembly original = Assembly.LoadFrom(args[0]);
        if (original.GetName().Name != "MyTime3")
            throw new InvalidOperationException("Expected original Aria assembly, not a rewrite.");
        Type encryption = original.GetType("MyTime3.Authorization.Encryption", true);
        Type login = original.GetType("MyTime3.Authorization.LoginManager", true);
        _filter = original.GetType("MyTime3.AuthorizeFilter", true);
        _encrypt = Required(encryption, "Encrypt", BindingFlags.Public | BindingFlags.Static,
            new Type[] { typeof(string) });
        _getRole = Required(login, "GetRole", BindingFlags.Public | BindingFlags.Static,
            new Type[] { typeof(HttpContextBase) });
        _getBadge = Required(login, "GetBadgeNumber", BindingFlags.Public | BindingFlags.Static,
            new Type[] { typeof(HttpContextBase) });
        _isExpired = Required(login, "IsExpired", BindingFlags.Public | BindingFlags.Static,
            new Type[] { typeof(HttpContextBase) });
        _authorize = Required(_filter, "AuthorizeCore", BindingFlags.NonPublic | BindingFlags.Instance,
            new Type[] { typeof(HttpContextBase) });

        HttpContextBase missing = Context(null);
        Check("AR-ORIG-SESSION-01 no cookie denies", missing, "", "", true, false, false);
        HttpContextBase user = Token(DateTime.Now.AddMinutes(-2), "4242", "user");
        Check("AR-ORIG-SESSION-02 user role and badge", user, "user", "4242", false, true, false);
        HttpContextBase admin = Token(DateTime.Now.AddMinutes(-2), "4242", "admin");
        Check("AR-ORIG-SESSION-03 role-only admin gate", admin, "admin", "4242", false, true, true);
        HttpContextBase old = Token(DateTime.Now.AddHours(-9), "4242", "admin");
        Check("AR-ORIG-SESSION-04 expired role/badge still readable but gate denies",
            old, "admin", "4242", true, false, false);
        Check("AR-ORIG-SESSION-05 malformed ciphertext denies", Context("invalid-cookie"),
            "", "", true, false, false);
        HttpContextBase shortPayload = Token(DateTime.Now.AddMinutes(-2), "4242", null);
        Check("AR-ORIG-SESSION-06 missing role denies despite unexpired timestamp",
            shortPayload, "", "4242", false, false, false);
        HttpContextBase future = Token(DateTime.Now.AddHours(1), "4242", "admin");
        Check("AR-ORIG-SESSION-07 future admin timestamp is accepted by original",
            future, "admin", "4242", false, true, true);

        Console.WriteLine("Summary: " + _passed + " Passed, 0 Failed (original TWF 4.28 binary; synthetic cookie methods/AuthorizeCore only)");
        return _passed == 7 ? 0 : 1;
    }

    private static MethodInfo Required(Type type, string name, BindingFlags flags, Type[] arguments)
    {
        MethodInfo method = type.GetMethod(name, flags, null, arguments, null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static HttpContextBase Token(DateTime issued, string badge, string role)
    {
        string payload = issued.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
            + ";" + badge + (role == null ? "" : ";" + role);
        string ciphertext = (string)_encrypt.Invoke(null, new object[] { payload });
        return Context(ciphertext);
    }

    private static HttpContextBase Context(string cookieValue)
    {
        // HttpContextWrapper supplies the Framework API the original code uses,
        // without hosting ASP.NET, opening a listener or reaching a database.
        HttpRequest request = new HttpRequest("", "http://localhost/", "");
        HttpResponse response = new HttpResponse(new StringWriter(CultureInfo.InvariantCulture));
        HttpContext context = new HttpContext(request, response);
        if (cookieValue != null)
            context.Request.Cookies.Add(new HttpCookie("mytimeauth", cookieValue));
        return new HttpContextWrapper(context);
    }

    private static bool Allowed(HttpContextBase context, string roles)
    {
        object filter = Activator.CreateInstance(_filter);
        _filter.GetProperty("Roles").SetValue(filter, roles, null);
        return (bool)_authorize.Invoke(filter, new object[] { context });
    }

    private static void Check(string name, HttpContextBase context, string role,
        string badge, bool expired, bool userGate, bool adminGate)
    {
        if ((string)_getRole.Invoke(null, new object[] { context }) != role ||
            (string)_getBadge.Invoke(null, new object[] { context }) != badge ||
            (bool)_isExpired.Invoke(null, new object[] { context }) != expired ||
            Allowed(context, "admin,user") != userGate || Allowed(context, "admin") != adminGate)
            throw new InvalidOperationException(name + ": original binary differed from the fixed expectation.");
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] " + name);
    }
}
