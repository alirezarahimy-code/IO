using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    [AdminGate(AdminFeature.PersonnelView, AllowReadOnly = true)]
    public sealed class PersonnelController : PortalController
    {
        public ActionResult Index(string term, int? departmentId, bool includeFormer = false)
        {
            var departments = PortalServices.Departments.BuildTree(PortalServices.Repository.GetDepartments());
            var model = new PersonnelIndexModel
            {
                Term = term ?? string.Empty,
                DepartmentId = departmentId,
                IncludeFormer = includeFormer,
                Rows = PortalServices.Personnel.Search(term, departmentId, includeFormer),
                Departments = departments.SelectMany(d => d.Flatten())
                    .OrderBy(d => d.DepartmentName, StringComparer.CurrentCulture)
                    .ToList(),
                CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.PersonnelEdit)
            };
            return View(model);
        }

        [AdminGate(AdminFeature.PersonnelEdit)]
        public ActionResult Edit(long id)
        {
            PersonnelRow row = PortalServices.Personnel.Get(id);
            if (row == null)
            {
                return HttpNotFound("پرسنل با این شماره یافت نشد.");
            }

            FillLookups();
            return View(row);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.PersonnelEdit)]
        public ActionResult Edit(PersonnelRow row)
        {
            // The form posts only the fields an administrator may change. Everything
            // else is reloaded from storage: posting HiredDate/FiredDate from a text
            // input would let a culture parsing difference silently blank them, and a
            // blank hire date corrupts every leave balance the engine computes.
            PersonnelRow stored = PortalServices.Personnel.Get(row.BadgeNumber);
            if (stored == null)
            {
                return HttpNotFound("پرسنل با این شماره یافت نشد.");
            }
            row.HiredDate = stored.HiredDate;
            row.FiredDate = stored.FiredDate;
            row.IsProgramUser = stored.IsProgramUser;
            row.UserName = stored.UserName;
            row.EmpTypeName = stored.EmpTypeName;
            row.DeptName = stored.DeptName;

            OperationResult result = PortalServices.Personnel.Save(row);
            if (!result.Succeeded)
            {
                FillLookups();
                ViewBag.Error = result.Message;
                return View(row);
            }

            Audit(AdminFeature.PersonnelEdit, "ویرایش پرسنل", "#" + row.BadgeNumber,
                PersonnelService.NormalizeDisplay(row.FullName) + " / واحد " + row.DeptId);
            TempData["Flash"] = "تغییرات پرسنل «" + PersonnelService.NormalizeDisplay(row.FullName) + "» ثبت شد.";
            return RedirectToAction("Index");
        }

        private void FillLookups()
        {
            ViewBag.Departments = DepartmentOptions();
            ViewBag.EmpTypes = PortalServices.Repository.GetEmpTypes()
                .Select(e => new SelectListItem { Value = e.Key.ToString(), Text = e.Value })
                .ToList();
        }

        private IReadOnlyList<SelectListItem> DepartmentOptions()
        {
            return PortalServices.Departments
                .BuildTree(PortalServices.Repository.GetDepartments())
                .SelectMany(d => d.Flatten())
                .OrderBy(d => d.DepartmentName, StringComparer.CurrentCulture)
                .Select(d => new SelectListItem
                {
                    Value = d.DepartmentId.ToString(),
                    Text = new string('·', Math.Max(0, d.Depth - 1)) + " " + d.DepartmentName
                })
                .ToList();
        }
    }

    public sealed class PersonnelIndexModel
    {
        public string Term { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public bool IncludeFormer { get; set; }
        public bool CanEdit { get; set; }
        public IReadOnlyList<PersonnelRow> Rows { get; set; } = new List<PersonnelRow>();
        public IReadOnlyList<DepartmentNode> Departments { get; set; } = new List<DepartmentNode>();
    }
}
