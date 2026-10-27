using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.Routing;

// Windows/.NET Framework probe against ONLY the pinned TWF 4.28 original
// MyTime3.dll. The original creates synthetic in-memory cookies; this program
// never prints their values, reuses a real cookie, calls Login, SQL or Settings,
// starts IIS, or copies original encryption into the net10 product. Route and
// action-attribute checks below are in-memory metadata, NOT HTTP execution.
// Logout is invoked on synthetic HttpContextWrapper only; browser behavior and
// server-side session revocation are not inferred from response cookie expiry.
internal static class Program
{
    private static int _passed;
    private static MethodInfo _encrypt;
    private static MethodInfo _getRole;
    private static MethodInfo _logout;
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
        _logout = Required(login, "Logout", BindingFlags.Public | BindingFlags.Static,
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
        HttpContextBase zeroBadge = Token(DateTime.Now.AddMinutes(-2), "0", "user");
        Check("AR-ORIG-SESSION-08 role gate ignores zero badge",
            zeroBadge, "user", "0", false, true, false);
        // Cookie.Expires is a browser-side attribute, not a server-side proof of
        // expiry when a client sends the value itself. Test the actual original
        // server-side boundary away from wall-clock/rounding edges.
        HttpContextBase justUnderEightHours = Token(DateTime.Now.AddMinutes(-475), "4242", "user");
        Check("AR-ORIG-SESSION-09 server accepts 7h55m-old supplied token",
            justUnderEightHours, "user", "4242", false, true, false);
        HttpContextBase overEightHours = Token(DateTime.Now.AddMinutes(-485), "4242", "user");
        Check("AR-ORIG-SESSION-10 server rejects 8h05m-old supplied token",
            overEightHours, "user", "4242", true, false, false);
        CheckOriginalDefaultRoute(original);
        CheckOriginalLoginMetadata(original);
        CheckOriginalUnauthorizedRedirect();
        CheckOriginalLogout();

        Console.WriteLine("Summary: " + _passed + " Passed, 0 Failed (original TWF 4.28 binary; in-memory cookie/route metadata only)");
        return _passed == 15 ? 0 : 1;
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

    private static void CheckOriginalDefaultRoute(Assembly original)
    {
        Type routesType = original.GetType("MyTime3.RouteConfig", true);
        MethodInfo register = routesType.GetMethod("RegisterRoutes", new Type[] { typeof(RouteCollection) });
        if (register == null || !register.IsStatic)
            throw new MissingMethodException("Original Aria RegisterRoutes missing.");
        // RouteCollection is created ONLY in this disposable Framework process.
        // RegisterRoutes does not initialize the web application or query a DB.
        var routes = new RouteCollection();
        register.Invoke(null, new object[] { routes });
        Route main = routes["Default"] as Route;
        if (main == null || main.Url != "{controller}/{action}/{id}" ||
            (string)main.Defaults["controller"] != "Home" ||
            (string)main.Defaults["action"] != "Index")
            throw new InvalidOperationException("AR-ORIG-ROUTE-11: original default route differs.");
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-ROUTE-11 default route metadata");
    }

    private static void CheckOriginalLoginMetadata(Assembly original)
    {
        Type login = original.GetType("MyTime3.Controllers.LoginController", true);
        Type model = original.GetType("MyTime3.Models.LoginModel", true);
        MethodInfo get = login.GetMethod("Login", new Type[0]);
        MethodInfo post = login.GetMethod("Login", new Type[] { model });
        MethodInfo testPassword = login.GetMethod("TestUserPass", new Type[] { model });
        MethodInfo change = login.GetMethod("Change",
            new Type[] { typeof(string), typeof(string), typeof(string) });
        if (!AttributePresent(get, "HttpGetAttribute") ||
            !AttributePresent(post, "HttpPostAttribute") ||
            !AttributePresent(testPassword, "HttpPostAttribute") ||
            !AttributePresent(change, "HttpPostAttribute") ||
            AttributePresent(testPassword, "AuthorizeFilter") ||
            AttributePresent(change, "AuthorizeFilter") ||
            AttributePresent(change, "ValidateAntiForgeryTokenAttribute"))
            throw new InvalidOperationException("AR-ORIG-ROUTE-12: original login action metadata differs.");
        // This reflects entry points ONLY. The original password checker and
        // edit-password action are NEVER invoked by this probe.
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-ROUTE-12 login GET/POST and unprotected credential POST metadata");
    }

    private static bool AttributePresent(MethodInfo method, string name)
    {
        if (method == null)
            throw new MissingMethodException("Original Aria login action missing.");
        foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
            if (attribute.AttributeType.Name == name) return true;
        return false;
    }

    private static void CheckOriginalUnauthorizedRedirect()
    {
        // Inspect the actual original filter behavior, without invoking an
        // HTTP action or executing a redirect. MVC5 AuthorizationContext is
        // constructed in memory with no request, host, service or database.
        MethodInfo handler = _filter.GetMethod("HandleUnauthorizedRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (handler == null || handler.GetParameters().Length != 1)
            throw new MissingMethodException("Original Aria unauthorized filter missing.");
        Type contextType = handler.GetParameters()[0].ParameterType;
        object context = Activator.CreateInstance(contextType);
        object filter = Activator.CreateInstance(_filter);
        handler.Invoke(filter, new object[] { context });
        PropertyInfo resultProperty = contextType.GetProperty("Result");
        object result = resultProperty == null ? null : resultProperty.GetValue(context, null);
        PropertyInfo url = result == null ? null : result.GetType().GetProperty("Url");
        if (url == null || (string)url.GetValue(result, null) != "~/Login/Login")
            throw new InvalidOperationException("AR-ORIG-ROUTE-13: original redirect target differs.");
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-ROUTE-13 filter redirects unauthorized to local login");
    }

    private static void CheckOriginalLogout()
    {
        // Verify the original only against disposable Framework request/response
        // objects. No real cookie, password, IIS request or server-side store.
        HttpContextBase anonymous = Context(null);
        _logout.Invoke(null, new object[] { anonymous });
        if (anonymous.Response.Cookies.Get("mytimeauth") != null)
            throw new InvalidOperationException("AR-ORIG-SESSION-14: absent request cookie caused a logout response cookie.");
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-SESSION-14 no request cookie: logout does not emit a cookie");

        HttpContextBase withCookie = Token(DateTime.Now.AddMinutes(-2), "4242", "user");
        _logout.Invoke(null, new object[] { withCookie });
        HttpCookie responseCookie = withCookie.Response.Cookies.Get("mytimeauth");
        if (responseCookie == null || responseCookie.Expires > DateTime.Now.AddHours(-23) ||
            (string)_getRole.Invoke(null, new object[] { withCookie }) != "user")
            throw new InvalidOperationException("AR-ORIG-SESSION-15: logout cookie expiry/request state differs.");
        // Request.Cookies still contains the same submitted token in this
        // in-memory request. Browser deletion and server revocation are NOT proved.
        _passed++;
        Console.WriteLine("[ORIGINAL-PASS] AR-ORIG-SESSION-15 response cookie expires; request token remains readable");
    }
}
