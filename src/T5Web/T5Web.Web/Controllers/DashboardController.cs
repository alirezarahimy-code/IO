using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The first screen an operator sees: is the system healthy, is a device dark,
    /// and what changed recently. Deliberately operational, not decorative.
    /// </summary>
    [AdminGate(AdminFeature.PersonnelView, AllowReadOnly = true)]
    public sealed class DashboardController : PortalController
    {
        public ActionResult Index()
        {
            AdminSession session = CurrentSession;
            IReadOnlyDictionary<string, int> counts = PortalServices.Repository.GetTableCounts();

            var departments = PortalServices.Departments.BuildTree(PortalServices.Repository.GetDepartments());
            IReadOnlyList<DeviceRow> devices = PortalServices.Repository.GetDevices(null);
            DateTime now = DateTime.UtcNow;

            var model = new DashboardModel
            {
                Personnel = counts.ContainsKey("UserInfo") ? counts["UserInfo"] : 0,
                Departments = counts.ContainsKey("Department") ? counts["Department"] : 0,
                Devices = counts.ContainsKey("Device") ? counts["Device"] : 0,
                WorkflowSteps = counts.ContainsKey("Workflow") ? counts["Workflow"] : 0,
                PendingPermissions = counts.ContainsKey("T5WebAccess") ? counts["T5WebAccess"] : 0,
                DepartmentDepth = PortalServices.Departments.MaxDepth(departments),
                SilentDevices = devices.Count(d => d.IsStale(now)),
                DeviceSamples = devices.Take(6).ToList(),
                RecentAudit = PortalServices.Audit.Recent(8),
                ChainBroken = PortalServices.Audit.VerifyChain(PortalServices.Audit.Recent(50)) >= 0,
                OperatorName = session == null ? "—" : session.DisplayName,
                OperatorTitle = session == null ? string.Empty : session.TitleName
            };
            return View(model);
        }
    }

    public sealed class DashboardModel
    {
        public int Personnel { get; set; }
        public int Departments { get; set; }
        public int Devices { get; set; }
        public int WorkflowSteps { get; set; }
        public int PendingPermissions { get; set; }
        public int DepartmentDepth { get; set; }
        public int SilentDevices { get; set; }
        public IReadOnlyList<DeviceRow> DeviceSamples { get; set; } = new List<DeviceRow>();
        public IReadOnlyList<AuditEntry> RecentAudit { get; set; } = new List<AuditEntry>();
        public bool ChainBroken { get; set; }
        public string OperatorName { get; set; } = string.Empty;
        public string OperatorTitle { get; set; } = string.Empty;
    }
}
