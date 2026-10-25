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
    /// The access matrix: one row per person, one column per feature.
    /// Deny is the default and is never written explicitly, so revoking is a delete.
    /// </summary>
    [AdminGate(AdminFeature.AccessView, AllowReadOnly = true)]
    public sealed class AccessController : PortalController
    {
        public ActionResult Index(string term)
        {
            var people = PortalServices.Personnel.Search(term, null, includeFormer: false, take: 40);
            var model = new AccessIndexModel
            {
                Term = term ?? string.Empty,
                People = people,
                Selected = people.FirstOrDefault(),
                Features = AdminFeatures.All().OrderBy(f => (int)f).ToList(),
                CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.AccessEdit)
            };

            if (model.Selected != null)
            {
                // Deny wins, exactly like AccessService.BuildSession: keep the lowest
                // level when the same feature appears twice (legacy duplicate rows).
                foreach (PermissionRow row in PortalServices.Access.Get(model.Selected.BadgeNumber))
                {
                    AccessLevel current;
                    if (!model.Levels.TryGetValue(row.Feature, out current) || row.Level < current)
                    {
                        model.Levels[row.Feature] = row.Level;
                    }
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.AccessEdit)]
        public ActionResult Set(long badgeNumber, AdminFeature feature, AccessLevel level)
        {
            OperationResult result = PortalServices.Access.Set(badgeNumber, feature, level);
            TempData["Flash"] = result.Succeeded
                ? "سطح دسترسی «" + AdminFeatures.TitleOf(feature) + "» به‌روزرسانی شد."
                : result.Message;

            Audit(AdminFeature.AccessEdit, "تغییر سطح دسترسی", "#" + badgeNumber,
                AdminFeatures.TitleOf(feature) + " -> " + level);
            return RedirectToAction("Index", new { term = badgeNumber.ToString() });
        }
    }

    public sealed class AccessIndexModel
    {
        public string Term { get; set; } = string.Empty;
        public bool CanEdit { get; set; }
        public IReadOnlyList<PersonnelRow> People { get; set; } = new List<PersonnelRow>();
        public PersonnelRow Selected { get; set; }
        public IReadOnlyList<AdminFeature> Features { get; set; } = new List<AdminFeature>();
        public Dictionary<AdminFeature, AccessLevel> Levels { get; set; } =
            new Dictionary<AdminFeature, AccessLevel>();
    }
}
