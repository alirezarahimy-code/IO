using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    [AdminGate(AdminFeature.WorkflowView, AllowReadOnly = true)]
    public sealed class WorkflowController : PortalController
    {
        public ActionResult Index(int workflowType = 1, int leaveTypeId = 0, int titleId = 0)
        {
            IReadOnlyList<WorkflowStep> steps = PortalServices.Workflow.Get(workflowType, leaveTypeId)
                .Where(s => titleId <= 0 || s.TitleId == titleId)
                .ToList();
            ViewBag.WorkflowType = workflowType;
            ViewBag.LeaveTypeId = leaveTypeId;
            ViewBag.TitleId = titleId;
            ViewBag.Titles = PortalServices.Repository.GetTitles();
            ViewBag.CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.WorkflowEdit);
            ViewBag.InFlight = PortalServices.Workflow.InFlight(workflowType, leaveTypeId);
            return View(steps);
        }

        /// <summary>
        /// Saves the whole chain posted from the grid. The controller rebuilds the step
        /// list from the form keys instead of trusting model binding order.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.WorkflowEdit)]
        public ActionResult Save(int workflowType, int leaveTypeId, int titleId,
                                 int[] accepterTitleId, int[] orderIndex, bool confirmStranded = false)
        {
            if (titleId <= 0)
            {
                TempData["Flash"] = "ابتدا عنوان درخواست‌کننده را انتخاب کنید.";
                return RedirectToAction("Index", new { workflowType, leaveTypeId });
            }
            if (accepterTitleId == null || orderIndex == null || accepterTitleId.Length != orderIndex.Length)
            {
                TempData["Flash"] = "داده‌های زنجیره تأیید ناقص است.";
                return RedirectToAction("Index", new { workflowType, leaveTypeId });
            }

            var steps = new List<WorkflowStep>();
            for (int i = 0; i < accepterTitleId.Length; i++)
            {
                if (accepterTitleId[i] <= 0)
                {
                    continue;
                }
                steps.Add(new WorkflowStep
                {
                    TitleId = titleId,
                    AccepterTitleId = accepterTitleId[i],
                    OrderIndex = orderIndex[i],
                    WorkflowType = workflowType,
                    LeaveTypeId = leaveTypeId
                });
            }

            OperationResult result = PortalServices.Workflow.Save(steps, workflowType, leaveTypeId, confirmStranded);
            TempData["Flash"] = result.Succeeded ? "زنجیره تأیید ذخیره شد." : result.Message;
            if (result.Succeeded)
            {
                string detail = steps.Count + " مرحله";
                if (confirmStranded)
                {
                    detail += "; تأیید صریح با وجود درخواست در جریان";
                }
                Audit(AdminFeature.WorkflowEdit, "ذخیره زنجیره تأیید",
                    "نوع " + workflowType + " / مرخصی " + leaveTypeId, detail);
            }
            return RedirectToAction("Index", new { workflowType, leaveTypeId, titleId });
        }
    }
}
