using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Web.Security
{
    /// <summary>
    /// Resolves the current Windows identity to a portal session.
    ///
    /// Authentication model (deliberately the same as the original T5V3Web, which
    /// the customer already runs):
    ///   * IIS negotiates the Windows account (no password form, nothing to phish);
    ///   * the account must exist in [UserInfo] with [WebUser] = 1;
    ///   * the *portal* rights come from [T5WebAccess], not from the Windows groups,
    ///     so an AD change cannot silently widen access.
    ///
    /// Every lookup is cached per request; the portal does not touch the session
    /// state server, so it stays a stateless web app.
    /// </summary>
    public static class AdminIdentity
    {
        private const string ItemKey = "__t5web_admin_session";
        private const string FailureKey = "__t5web_admin_failure";

        /// <summary>Returns the signed-in operator, or null when the request is anonymous.</summary>
        public static AdminSession Current
        {
            get
            {
                HttpContext context = HttpContext.Current;
                if (context == null)
                {
                    return null;
                }

                var cached = context.Items[ItemKey] as AdminSession;
                if (cached != null)
                {
                    return cached;
                }

                string failure;
                AdminSession session = Resolve(context, out failure);
                context.Items[ItemKey] = session;
                context.Items[FailureKey] = failure ?? string.Empty;
                return session;
            }
        }

        /// <summary>Why the last resolution failed (shown on the "no access" page).</summary>
        public static string LastFailure
        {
            get
            {
                HttpContext context = HttpContext.Current;
                return context == null ? string.Empty : (context.Items[FailureKey] as string ?? string.Empty);
            }
        }

        private static AdminSession Resolve(HttpContext context, out string failure)
        {
            failure = string.Empty;

            string raw = context.User != null && context.User.Identity != null
                ? context.User.Identity.Name
                : string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
            {
                failure = "برای ورود به پورتال باید با حساب ویندوز سازمانی وارد شوید.";
                return null;
            }

            string login = Normalize(raw);
            IAdminRepository repository = PortalServices.Repository;
            if (repository == null)
            {
                failure = "اتصال پایگاه‌داده پیکربندی نشده است.";
                return null;
            }

            PersonnelRow person = repository.FindUserByUserName(login);
            if (person == null)
            {
                failure = "حساب ویندوزی «" + raw + "» در پرسنل ثبت نشده است.";
                return null;
            }
            if (person.IsFormer)
            {
                failure = "این حساب متعلق به پرسنل پایان‌یافته است.";
                return null;
            }
            if (!person.IsWebUser)
            {
                failure = "دسترسی وب برای این حساب فعال نیست.";
                return null;
            }

            var access = new AccessService(repository);
            AdminSession session = access.BuildSession(person, repository.GetPermissions(person.BadgeNumber));
            if (!session.IsPortalAdmin && session.Levels.Count == 0)
            {
                failure = "هیچ سطح دسترسی برای این حساب تعریف نشده است.";
                return null;
            }
            return session;
        }

        /// <summary>Accepts "DOMAIN\user", "user@domain" and plain "user".</summary>
        public static string Normalize(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return string.Empty;
            }
            string value = identity.Trim();
            int slash = value.LastIndexOf('\\');
            if (slash >= 0 && slash + 1 < value.Length)
            {
                value = value.Substring(slash + 1);
            }
            int at = value.IndexOf('@');
            if (at > 0)
            {
                value = value.Substring(0, at);
            }
            return value;
        }
    }

    /// <summary>
    /// Runs before the action and refuses access unless the operator holds the
    /// required level for a feature. Denies by default: a controller without the
    /// attribute still needs an authenticated session.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class AdminGateAttribute : AuthorizeAttribute
    {
        public AdminGateAttribute(AdminFeature feature)
        {
            Feature = feature;
            Features = new[] { feature };
        }

        /// <summary>
        /// Gate on *any* of several capabilities.
        ///
        /// Used only where one screen hosts modules that belong to different features
        /// (the generic module screens: base data, leave types, rules, devices). The
        /// action then narrows to the module's own feature before touching data, so a
        /// broad gate can never widen access.
        /// </summary>
        public AdminGateAttribute(params AdminFeature[] anyOf)
        {
            Features = anyOf == null || anyOf.Length == 0
                ? new[] { AdminFeature.PortalAdmin }
                : anyOf;
            Feature = Features[0];
        }

        public AdminFeature Feature { get; private set; }

        /// <summary>Every capability that opens this gate (one, normally).</summary>
        public IReadOnlyList<AdminFeature> Features { get; private set; }

        /// <summary>When true, ReadOnly is enough; otherwise Allow is required.</summary>
        public bool AllowReadOnly { get; set; }

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            if (httpContext == null)
            {
                return false;
            }

            AdminSession session = AdminIdentity.Current;
            if (session == null)
            {
                return false;
            }

            foreach (AdminFeature feature in Features)
            {
                AccessLevel level = session.LevelOf(feature);
                if (level == AccessLevel.Allow)
                {
                    return true;
                }
                if (level == AccessLevel.ReadOnly && AllowReadOnly)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            if (filterContext == null)
            {
                return;
            }

            AdminSession session = AdminIdentity.Current;
            if (session == null)
            {
                filterContext.Result = new ViewResult
                {
                    ViewName = "~/Views/Account/NoAccess.cshtml",
                    ViewData = new ViewDataDictionary { { "Reason", AdminIdentity.LastFailure } },
                    TempData = filterContext.Controller.TempData
                };
                filterContext.HttpContext.Response.StatusCode = 401;
                filterContext.HttpContext.Response.SuppressFormsAuthenticationRedirect = true;
                return;
            }

            string titles = string.Join("» یا «", Features.Select(AdminFeatures.TitleOf));
            filterContext.Result = new ViewResult
            {
                ViewName = "~/Views/Account/NoAccess.cshtml",
                ViewData = new ViewDataDictionary { { "Reason", "سطح دسترسی لازم برای «" + titles + "» را ندارید." } },
                TempData = filterContext.Controller.TempData
            };
            filterContext.HttpContext.Response.StatusCode = 403;
        }
    }

    /// <summary>Convenience base class so every admin controller gets the same shape.</summary>
    public abstract class PortalController : Controller
    {
        protected AdminSession CurrentSession
        {
            get { return AdminIdentity.Current; }
        }

        protected ActionResult Denied(AdminFeature feature)
        {
            return View("~/Views/Account/NoAccess.cshtml", new ViewDataDictionary
            {
                { "Reason", "سطح دسترسی لازم برای «" + AdminFeatures.TitleOf(feature) + "» را ندارید." }
            });
        }

        /// <summary>Audits a write and returns the audit entry for the view.</summary>
        protected AuditEntry Audit(AdminFeature feature, string action, string target, string detail)
        {
            AdminSession session = CurrentSession;
            return PortalServices.Audit.Record(
                session == null ? "anonymous" : session.DisplayName + " (" + session.BadgeNumber + ")",
                feature,
                action,
                target,
                detail);
        }
    }

    /// <summary>
    /// Process-wide service locator, wired once in MvcApplication.
    ///
    /// Only four singletons; constructor injection is used everywhere below this
    /// layer, so the tests build services directly and never touch this class.
    /// </summary>
    public static class PortalServices
    {
        private static readonly object SyncRoot = new object();
        private static IAdminRepository _repository;
        private static PersonnelService _personnel;
        private static DepartmentService _departments;
        private static AccessService _access;
        private static WorkflowService _workflow;
        private static AuditService _audit;
        private static T5Web.Application.Modules.ModuleService _modules;

        /// <summary>
        /// Wires the process-wide services. The module store is optional: a deployment
        /// that only runs the classic screens does not need it, and the module screens
        /// then answer with the "not configured" page instead of a NullReferenceException.
        /// </summary>
        public static void Configure(IAdminRepository repository)
        {
            Configure(repository, null);
        }

        public static void Configure(IAdminRepository repository, IModuleStore moduleStore)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            lock (SyncRoot)
            {
                _repository = repository;
                _personnel = new PersonnelService(repository);
                _departments = new DepartmentService();
                _access = new AccessService(repository);
                _workflow = new WorkflowService(repository);
                _audit = new AuditService(repository);
                _modules = moduleStore == null ? null : new T5Web.Application.Modules.ModuleService(moduleStore);
            }
        }

        public static IAdminRepository Repository
        {
            get { lock (SyncRoot) { return _repository; } }
        }

        public static PersonnelService Personnel
        {
            get { lock (SyncRoot) { return _personnel; } }
        }

        public static DepartmentService Departments
        {
            get { lock (SyncRoot) { return _departments; } }
        }

        public static AccessService Access
        {
            get { lock (SyncRoot) { return _access; } }
        }

        public static WorkflowService Workflow
        {
            get { lock (SyncRoot) { return _workflow; } }
        }

        public static AuditService Audit
        {
            get { lock (SyncRoot) { return _audit; } }
        }

        public static T5Web.Application.Modules.ModuleService Modules
        {
            get { lock (SyncRoot) { return _modules; } }
        }

        /// <summary>Menu entries the current operator can actually open.</summary>
        public static IReadOnlyList<MenuEntry> MenuFor(AdminSession session)
        {
            var menu = new List<MenuEntry>();
            if (session == null)
            {
                return menu;
            }

            Add(menu, session, AdminFeature.PersonnelView, "پرسنل", "Personnel", "Index");
            Add(menu, session, AdminFeature.DepartmentView, "چارت سازمانی", "Department", "Index");
            Add(menu, session, AdminFeature.DeviceView, "دستگاه‌ها", "Device", "Index");
            Add(menu, session, AdminFeature.RecordsView, "ترددها", "Records", "Index");
            Add(menu, session, AdminFeature.RulesView, "قواعد حضور", "Rules", "Index");
            Add(menu, session, AdminFeature.ScheduleView, "شیفت و برنامه", "Schedule", "Index");
            Add(menu, session, AdminFeature.LeaveView, "مرخصی و مانده", "Leave", "Index");
            Add(menu, session, AdminFeature.RequestView, "درخواست‌ها", "Requests", "Index");
            Add(menu, session, AdminFeature.ReportView, "گزارش‌ها", "Reports", "Index");
            Add(menu, session, AdminFeature.BaseDataView, "داده پایه", "Modules", "Index");
            Add(menu, session, AdminFeature.PersonnelImport, "ورود از فایل", "Import", "Index");
            Add(menu, session, AdminFeature.AccessView, "سطوح دسترسی", "Access", "Index");
            Add(menu, session, AdminFeature.WorkflowView, "گردش کار", "Workflow", "Index");
            Add(menu, session, AdminFeature.LogView, "رخدادها", "Audit", "Index");
            if (session.IsPortalAdmin)
            {
                menu.Add(new MenuEntry { Title = "پایش سامانه", Controller = "Diagnostics", Action = "Index" });
            }
            return menu;
        }

        private static void Add(List<MenuEntry> menu, AdminSession session, AdminFeature feature,
                                string title, string controller, string action)
        {
            if (session.CanView(feature))
            {
                menu.Add(new MenuEntry { Title = title, Controller = controller, Action = action });
            }
        }
    }

    /// <summary>
    /// When the portal cannot reach its configuration, every request shows one
    /// readable Persian page instead of a NullReferenceException inside a controller.
    /// </summary>
    public sealed class BootstrapFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (filterContext == null || PortalServices.Repository != null)
            {
                return;
            }

            string controller = filterContext.RouteData != null &&
                                filterContext.RouteData.Values.ContainsKey("controller")
                ? (filterContext.RouteData.Values["controller"] as string ?? string.Empty)
                : string.Empty;

            if (string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            filterContext.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/NotConfigured.cshtml",
                ViewData = new ViewDataDictionary { { "Reason", MvcApplication.StartupError } }
            };
            filterContext.HttpContext.Response.StatusCode = 503;
        }
    }

    public sealed class MenuEntry
    {
        public string Title { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
    }
}
