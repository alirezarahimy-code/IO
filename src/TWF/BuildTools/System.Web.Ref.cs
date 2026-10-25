using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Security.Principal;

namespace System.Web
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class PreApplicationStartMethodAttribute : Attribute
    {
        public Type Type { get; private set; }
        public string MethodName { get; private set; }

        public PreApplicationStartMethodAttribute(Type type, string methodName)
        {
            Type = type;
            MethodName = methodName;
        }
    }

    public static class VirtualPathUtility
    {
        public static string ToAppRelative(string virtualPath) => virtualPath ?? "~/";
        public static string ToAppRelative(string virtualPath, string applicationPath) => virtualPath ?? "~/";
        public static string ToAbsolute(string virtualPath) => (virtualPath ?? "/").Replace("~/", "/");
        public static string ToAbsolute(string virtualPath, string applicationPath) => (virtualPath ?? "/").Replace("~/", "/");
        public static string GetExtension(string virtualPath) => Path.GetExtension(virtualPath ?? "");
        public static string GetFileName(string virtualPath) => Path.GetFileName(virtualPath ?? "");
        public static string GetDirectory(string virtualPath) => Path.GetDirectoryName(virtualPath ?? "") ?? "/";
        public static string MakeRelative(string fromPath, string toPath) => toPath ?? "";
        public static string Combine(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(basePath)) return relativePath ?? "";
            if (string.IsNullOrEmpty(relativePath)) return basePath;
            return basePath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }
        public static string AppendTrailingSlash(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath)) return "/";
            return virtualPath.EndsWith("/") ? virtualPath : virtualPath + "/";
        }
        public static string RemoveTrailingSlash(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/") return virtualPath;
            return virtualPath.TrimEnd('/');
        }
        public static bool IsAppRelative(string virtualPath) => !string.IsNullOrEmpty(virtualPath) && virtualPath.StartsWith("~/");
        public static bool IsAbsolute(string virtualPath) => !string.IsNullOrEmpty(virtualPath) && virtualPath.StartsWith("/");
    }

    public enum SameSiteMode
    {
        None = 0,
        Lax = 1,
        Strict = 2
    }

    public interface IHtmlString
    {
        string ToHtmlString();
    }

    public class HtmlString : IHtmlString
    {
        private readonly string _html;
        public HtmlString(string value) { _html = value ?? ""; }
        public string ToHtmlString() => _html;
        public override string ToString() => _html;
    }

    public interface IHttpHandler
    {
        bool IsReusable { get; }
        void ProcessRequest(HttpContext context);
    }

    public interface IHttpModule
    {
        void Init(HttpApplication context);
        void Dispose();
    }

    public class HttpCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public DateTime Expires { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public SameSiteMode SameSite { get; set; }
        public string Path { get; set; } = "/";
        public string Domain { get; set; }

        public HttpCookie(string name) { Name = name; }
        public HttpCookie(string name, string value) { Name = name; Value = value; }
    }

    public sealed class HttpCookieCollection : NameObjectCollectionBase
    {
        private readonly Dictionary<string, HttpCookie> _cookies = new Dictionary<string, HttpCookie>(StringComparer.OrdinalIgnoreCase);

        public HttpCookie this[string name]
        {
            get => Get(name);
        }

        public HttpCookie Get(string name)
        {
            return _cookies.TryGetValue(name, out var c) ? c : null;
        }

        public void Add(HttpCookie cookie)
        {
            if (cookie != null && cookie.Name != null)
                _cookies[cookie.Name] = cookie;
        }

        public void Set(HttpCookie cookie) => Add(cookie);
        public void Remove(string name) => _cookies.Remove(name);
    }

    public class HttpServerUtility
    {
        public string MapPath(string path) => path ?? "";
    }

    public abstract class HttpServerUtilityBase
    {
        public virtual string MapPath(string path) => path ?? "";
    }

    public class HttpServerUtilityWrapper : HttpServerUtilityBase
    {
        private readonly HttpServerUtility _server;
        public HttpServerUtilityWrapper(HttpServerUtility httpServerUtility) { _server = httpServerUtility; }
        public override string MapPath(string path) => _server != null ? _server.MapPath(path) : (path ?? "");
    }

    public class HttpRequest
    {
        public NameValueCollection Params { get; } = new NameValueCollection();
        public NameValueCollection QueryString { get; } = new NameValueCollection();
        public NameValueCollection Form { get; } = new NameValueCollection();
        public NameValueCollection Headers { get; } = new NameValueCollection();
        public HttpCookieCollection Cookies { get; } = new HttpCookieCollection();
        public string UserHostAddress { get; set; } = "127.0.0.1";
        public string UserAgent { get; set; } = "TWF-TestAgent";
        public string HttpMethod { get; set; } = "GET";
        public Uri Url { get; set; } = new Uri("http://localhost/");
        public string this[string key] => Params[key];
    }

    public abstract class HttpRequestBase
    {
        public virtual NameValueCollection Params { get; } = new NameValueCollection();
        public virtual NameValueCollection QueryString { get; } = new NameValueCollection();
        public virtual NameValueCollection Form { get; } = new NameValueCollection();
        public virtual NameValueCollection Headers { get; } = new NameValueCollection();
        public virtual HttpCookieCollection Cookies { get; } = new HttpCookieCollection();
        public virtual string UserHostAddress => "127.0.0.1";
        public virtual string UserAgent => "TWF-TestAgent";
        public virtual string HttpMethod => "GET";
        public virtual Uri Url => new Uri("http://localhost/");
        public virtual string this[string key] => Params[key];
    }

    public class HttpRequestWrapper : HttpRequestBase
    {
        private readonly HttpRequest _req;
        public HttpRequestWrapper(HttpRequest httpRequest) { _req = httpRequest ?? new HttpRequest(); }
        public override NameValueCollection Params => _req.Params;
        public override NameValueCollection QueryString => _req.QueryString;
        public override NameValueCollection Form => _req.Form;
        public override NameValueCollection Headers => _req.Headers;
        public override HttpCookieCollection Cookies => _req.Cookies;
        public override string UserHostAddress => _req.UserHostAddress;
        public override string UserAgent => _req.UserAgent;
        public override string HttpMethod => _req.HttpMethod;
        public override Uri Url => _req.Url;
        public override string this[string key] => _req[key];
    }

    public class HttpResponse
    {
        public HttpCookieCollection Cookies { get; } = new HttpCookieCollection();
        public int StatusCode { get; set; } = 200;
        public void AppendCookie(HttpCookie cookie) => Cookies.Add(cookie);
        public void Redirect(string url) { }
    }

    public abstract class HttpResponseBase
    {
        public virtual HttpCookieCollection Cookies { get; } = new HttpCookieCollection();
        public virtual int StatusCode { get; set; } = 200;
        public virtual void AppendCookie(HttpCookie cookie) => Cookies.Add(cookie);
        public virtual void Redirect(string url) { }
    }

    public class HttpResponseWrapper : HttpResponseBase
    {
        private readonly HttpResponse _res;
        public HttpResponseWrapper(HttpResponse httpResponse) { _res = httpResponse ?? new HttpResponse(); }
        public override HttpCookieCollection Cookies => _res.Cookies;
        public override int StatusCode { get => _res.StatusCode; set => _res.StatusCode = value; }
        public override void AppendCookie(HttpCookie cookie) => _res.AppendCookie(cookie);
        public override void Redirect(string url) => _res.Redirect(url);
    }

    public abstract class HttpSessionStateBase
    {
        private readonly Dictionary<string, object> _session = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public virtual object this[string name]
        {
            get => _session.TryGetValue(name, out var v) ? v : null;
            set => _session[name] = value;
        }
    }

    public class HttpContext
    {
        public static HttpContext Current { get; set; } = new HttpContext();
        public HttpRequest Request { get; set; } = new HttpRequest();
        public HttpResponse Response { get; set; } = new HttpResponse();
        public HttpServerUtility Server { get; set; } = new HttpServerUtility();
        public IPrincipal User { get; set; }
        public IDictionary Items { get; } = new Dictionary<object, object>();
        public bool IsDebuggingEnabled => false;
    }

    public abstract class HttpContextBase
    {
        public virtual HttpRequestBase Request { get; } = new HttpRequestWrapper(new HttpRequest());
        public virtual HttpResponseBase Response { get; } = new HttpResponseWrapper(new HttpResponse());
        public virtual HttpServerUtilityBase Server { get; } = new HttpServerUtilityWrapper(new HttpServerUtility());
        public virtual HttpSessionStateBase Session { get; }
        public virtual IPrincipal User { get; set; }
        public virtual IDictionary Items { get; } = new Dictionary<object, object>();
        public virtual bool IsDebuggingEnabled => false;
    }

    public class HttpContextWrapper : HttpContextBase
    {
        private readonly HttpContext _ctx;
        private readonly HttpRequestBase _req;
        private readonly HttpResponseBase _res;
        private readonly HttpServerUtilityBase _srv;

        public HttpContextWrapper(HttpContext httpContext)
        {
            _ctx = httpContext ?? new HttpContext();
            _req = new HttpRequestWrapper(_ctx.Request);
            _res = new HttpResponseWrapper(_ctx.Response);
            _srv = new HttpServerUtilityWrapper(_ctx.Server);
        }

        public override HttpRequestBase Request => _req;
        public override HttpResponseBase Response => _res;
        public override HttpServerUtilityBase Server => _srv;
        public override IPrincipal User { get => _ctx.User; set => _ctx.User = value; }
        public override IDictionary Items => _ctx.Items;
        public override bool IsDebuggingEnabled => _ctx.IsDebuggingEnabled;
    }

    public class HttpApplication : IHttpHandler
    {
        public HttpContext Context => HttpContext.Current;
        public HttpRequest Request => HttpContext.Current?.Request;
        public HttpResponse Response => HttpContext.Current?.Response;
        public HttpServerUtility Server => HttpContext.Current?.Server;
        public virtual bool IsReusable => false;
        public virtual void ProcessRequest(HttpContext context) { }
    }

    public static class HttpUtility
    {
        public static string UrlDecode(string str) => str == null ? null : Uri.UnescapeDataString(str.Replace("+", " "));
        public static string UrlEncode(string str) => str == null ? null : Uri.EscapeDataString(str);
        public static string HtmlEncode(string s) => s ?? "";
        public static string HtmlDecode(string s) => s ?? "";
    }
}

namespace System.Web.Configuration
{
    public static class WebConfigurationManager
    {
        public static NameValueCollection AppSettings => ConfigurationManager.AppSettings;
        public static ConnectionStringSettingsCollection ConnectionStrings => ConfigurationManager.ConnectionStrings;
        public static System.Configuration.Configuration OpenWebConfiguration(string path) => new System.Configuration.Configuration();
    }
}

namespace System.Web.Hosting
{
    public abstract class VirtualFileBase : MarshalByRefObject
    {
        public virtual string Name => "";
        public string VirtualPath { get; }
        protected VirtualFileBase() { }
        protected VirtualFileBase(string virtualPath) { VirtualPath = virtualPath; }
        public abstract bool IsDirectory { get; }
    }

    public abstract class VirtualFile : VirtualFileBase
    {
        protected VirtualFile(string virtualPath) : base(virtualPath) { }
        public override bool IsDirectory => false;
        public abstract Stream Open();
    }

    public abstract class VirtualDirectory : VirtualFileBase
    {
        protected VirtualDirectory(string virtualPath) : base(virtualPath) { }
        public override bool IsDirectory => true;
    }

    public abstract class VirtualPathProvider : MarshalByRefObject
    {
        public virtual VirtualPathProvider Previous => null;
        protected VirtualPathProvider() { }
        public virtual bool FileExists(string virtualPath) => false;
        public virtual VirtualFile GetFile(string virtualPath) => null;
    }

    public static class HostingEnvironment
    {
        public static void RegisterVirtualPathProvider(VirtualPathProvider virtualPathProvider) { }
        public static string MapPath(string virtualPath) => virtualPath ?? "";
    }
}

namespace System.Web.Routing
{
    public interface IRouteHandler
    {
        IHttpHandler GetHttpHandler(RequestContext requestContext);
    }

    public interface IRouteConstraint
    {
        bool Match(HttpContextBase httpContext, Route route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection);
    }

    public enum RouteDirection { IncomingRequest, UrlGeneration }

    public class RouteValueDictionary : Dictionary<string, object>
    {
        public RouteValueDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
        public RouteValueDictionary(object values) : base(StringComparer.OrdinalIgnoreCase)
        {
            if (values != null)
            {
                if (values is IDictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                        base[kvp.Key] = kvp.Value;
                }
                else
                {
                    foreach (var prop in values.GetType().GetProperties())
                    {
                        if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                        {
                            base[prop.Name] = prop.GetValue(values, null);
                        }
                    }
                }
            }
        }
        public RouteValueDictionary(IDictionary<string, object> dictionary) : base(dictionary, StringComparer.OrdinalIgnoreCase) { }

        public new object this[string key]
        {
            get => TryGetValue(key, out var val) ? val : null;
            set => base[key] = value;
        }
    }

    public class VirtualPathData
    {
        public RouteBase Route { get; set; }
        public string VirtualPath { get; set; }
        public RouteValueDictionary DataTokens { get; } = new RouteValueDictionary();
        public VirtualPathData(RouteBase route, string virtualPath) { Route = route; VirtualPath = virtualPath; }
    }

    public class RouteData
    {
        public RouteBase Route { get; set; }
        public IRouteHandler RouteHandler { get; set; }
        public RouteValueDictionary Values { get; } = new RouteValueDictionary();
        public RouteValueDictionary DataTokens { get; } = new RouteValueDictionary();
        public RouteData() { }
        public RouteData(RouteBase route, IRouteHandler routeHandler) { Route = route; RouteHandler = routeHandler; }
    }

    public class RequestContext
    {
        public HttpContextBase HttpContext { get; set; }
        public RouteData RouteData { get; set; }
        public RequestContext() { }
        public RequestContext(HttpContextBase httpContext, RouteData routeData) { HttpContext = httpContext; RouteData = routeData; }
    }

    public abstract class RouteBase
    {
        public abstract RouteData GetRouteData(HttpContextBase httpContext);
        public abstract VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values);
    }

    public class Route : RouteBase
    {
        public string Url { get; set; }
        public RouteValueDictionary Defaults { get; set; }
        public RouteValueDictionary Constraints { get; set; }
        public RouteValueDictionary DataTokens { get; set; }
        public IRouteHandler RouteHandler { get; set; }
        public Route(string url, IRouteHandler routeHandler) { Url = url; RouteHandler = routeHandler; }
        public Route(string url, RouteValueDictionary defaults, IRouteHandler routeHandler) { Url = url; Defaults = defaults; RouteHandler = routeHandler; }
        public Route(string url, RouteValueDictionary defaults, RouteValueDictionary constraints, IRouteHandler routeHandler) { Url = url; Defaults = defaults; Constraints = constraints; RouteHandler = routeHandler; }
        public Route(string url, RouteValueDictionary defaults, RouteValueDictionary constraints, RouteValueDictionary dataTokens, IRouteHandler routeHandler) { Url = url; Defaults = defaults; Constraints = constraints; DataTokens = dataTokens; RouteHandler = routeHandler; }
        public override RouteData GetRouteData(HttpContextBase httpContext) => null;
        public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values) => null;
    }

    public class RouteCollection : System.Collections.ObjectModel.Collection<RouteBase>
    {
        public void Add(string name, RouteBase item) => base.Add(item);
        public void Ignore(string url) { }
    }

    public static class RouteTable
    {
        public static RouteCollection Routes { get; } = new RouteCollection();
    }
}

namespace System.Web.UI.HtmlControls
{
    public class HtmlForm : System.Web.UI.Control { }
}

namespace System.Web.UI
{
    public class Control
    {
        public virtual string ID { get; set; }
    }

    public class Page : Control, IHttpHandler
    {
        public bool IsPostBack => false;
        public HttpRequest Request => HttpContext.Current?.Request;
        public HttpResponse Response => HttpContext.Current?.Response;
        public virtual bool IsReusable => false;
        public virtual void ProcessRequest(HttpContext context) { }
    }
}
