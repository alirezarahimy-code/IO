using System;
using System.Collections.Generic;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    [AdminGate(AdminFeature.DepartmentView, AllowReadOnly = true)]
    public sealed class DepartmentController : PortalController
    {
        public ActionResult Index()
        {
            var flat = PortalServices.Repository.GetDepartments();
            var roots = PortalServices.Departments.BuildTree(flat);

            ViewBag.Flat = flat;
            ViewBag.MaxDepth = PortalServices.Departments.MaxDepth(roots);
            ViewBag.TotalMembers = PortalServices.Departments.TotalMembers(roots);
            ViewBag.CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.DepartmentEdit);
            return View(roots);
        }

        /// <summary>
        /// Reparents one department. The cycle check runs before the write, so a bad
        /// drag can never make the chart unrenderable.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.DepartmentEdit)]
        public ActionResult Move(int departmentId, int newParentId)
        {
            var flat = PortalServices.Repository.GetDepartments();
            if (PortalServices.Departments.WouldCreateCycle(flat, departmentId, newParentId))
            {
                TempData["Flash"] = "جابه‌جایی رد شد: این تغییر باعث ایجاد حلقه در چارت سازمانی می‌شود.";
                return RedirectToAction("Index");
            }

            OperationResult result = PortalServices.Repository.MoveDepartment(departmentId, newParentId);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index");
            }

            Audit(AdminFeature.DepartmentEdit, "جابه‌جایی واحد", "واحد " + departmentId,
                "والد جدید: " + newParentId);
            TempData["Flash"] = "جابه‌جایی واحد ثبت شد.";
            return RedirectToAction("Index");
        }
    }
}
