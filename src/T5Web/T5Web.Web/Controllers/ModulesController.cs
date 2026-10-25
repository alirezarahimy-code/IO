using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using T5Web.Application.Modules;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The generic CRUD screens: one action set for every table that the legacy portal
    /// opened with the same four actions (Index / Edit / Delete / DeleteAll) in a
    /// controller of its own.
    ///
    /// Authorization has two layers on purpose:
    ///   * the class-level gate is a coarse "you may see module screens at all";
    ///   * every action then narrows to the module's own feature from its descriptor,
    ///     so the coarse gate can never widen access (a device operator cannot reach
    ///     the parameters screen by guessing ?code=parameter).
    /// </summary>
    [AdminGate(AdminFeature.BaseDataView, AdminFeature.LeaveView, AdminFeature.RulesView,
               AdminFeature.DeviceView, AdminFeature.TitlesView, AdminFeature.GateView,
               // Wave 2/3 (docs/11): employment-type entitlements, personnel files and the
               // engine's own access tables also live on this screen. The class-level gate
               // stays coarse on purpose - every action re-checks the module's own feature.
               AdminFeature.PersonnelView, AdminFeature.AccessView)]
    public sealed class ModulesController : PortalController
    {
        private const int PageSize = 50;

        public ActionResult Index(string code, string term, int page = 1)
        {
            // No code means "the rail item was clicked": open the first module this
            // operator may actually see. A wrong *explicit* code still 404s, so a stale
            // bookmark can never silently show a different table (docs/11).
            ModuleDescriptor descriptor = string.IsNullOrWhiteSpace(code) ? FirstVisible() : Resolve(code);
            if (descriptor == null)
            {
                return string.IsNullOrWhiteSpace(code)
                    ? Denied(AdminFeature.BaseDataView)
                    : HttpNotFound("ماژول خواسته‌شده وجود ندارد.");
            }
            if (!CanView(descriptor))
            {
                return Denied(descriptor.ViewFeature);
            }
            if (PortalServices.Modules == null)
            {
                return View("~/Views/Shared/NotConfigured.cshtml",
                    new ViewDataDictionary { { "Reason", "مخزن ماژول‌ها تنظیم نشده است." } });
            }

            var model = new ModuleIndexModel
            {
                Descriptor = descriptor,
                Page = PortalServices.Modules.Page(descriptor, term, page, PageSize),
                Groups = ModulesFor(descriptor),
                CanEdit = !descriptor.ReadOnly && CurrentSession != null && CurrentSession.CanEdit(descriptor.EditFeature),
                CanUpdate = descriptor.CanUpdate
            };
            ViewBag.Title = descriptor.TitleFa;
            return View(model);
        }

        public ActionResult Edit(string code, string key)
        {
            ModuleDescriptor descriptor = Resolve(code);
            if (descriptor == null)
            {
                return HttpNotFound("ماژول خواسته‌شده وجود ندارد.");
            }
            if (!CanView(descriptor))
            {
                return Denied(descriptor.ViewFeature);
            }

            if (descriptor.ReadOnly)
            {
                return Denied(descriptor.EditFeature);
            }

            bool isNew = string.IsNullOrWhiteSpace(key);
            if (isNew && !descriptor.AllowCreate)
            {
                return HttpNotFound("افزودن سطر جدید در این ماژول مجاز نیست.");
            }
            if (!isNew && !descriptor.CanUpdate)
            {
                // Keys-only link tables: the row *is* its key, so there is nothing to edit.
                return HttpNotFound("این ماژول ستون قابل ویرایش ندارد؛ سطر را حذف و دوباره اضافه کنید.");
            }
            if (!isNew && !(CurrentSession != null && CurrentSession.CanEdit(descriptor.EditFeature)))
            {
                return Denied(descriptor.EditFeature);
            }
            if (isNew && !(CurrentSession != null && CurrentSession.CanEdit(descriptor.EditFeature)))
            {
                return Denied(descriptor.EditFeature);
            }

            ModuleRow row = isNew ? NewRow(descriptor) : PortalServices.Modules.Get(descriptor, ParseKey(descriptor, key));
            if (row == null)
            {
                return HttpNotFound("سطر مورد نظر یافت نشد.");
            }

            ViewBag.Title = (isNew ? "افزودن — " : "ویرایش — ") + descriptor.TitleFa;
            return View(new ModuleEditModel
            {
                Descriptor = descriptor,
                Row = row,
                IsNew = isNew,
                Key = key ?? string.Empty,
                CanDelete = !isNew && descriptor.AllowDelete,
                FormAction = Url.Action("Edit", "Modules", new { code = descriptor.Code })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(string code, FormCollection form)
        {
            ModuleDescriptor descriptor = Resolve(code);
            if (descriptor == null)
            {
                return HttpNotFound("ماژول خواسته‌شده وجود ندارد.");
            }
            if (!CanView(descriptor))
            {
                return Denied(descriptor.ViewFeature);
            }
            if (descriptor.ReadOnly || !(CurrentSession != null && CurrentSession.CanEdit(descriptor.EditFeature)))
            {
                return Denied(descriptor.EditFeature);
            }

            bool isNew = string.Equals(form["__isNew"], "true", StringComparison.OrdinalIgnoreCase);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleField field in descriptor.Fields)
            {
                values[field.Column] = form[field.Column];
            }

            ModuleRow row;
            OperationResult built = PortalServices.Modules.Build(descriptor, values, isNew, out row);
            if (!built.Succeeded)
            {
                return EditFailed(descriptor, row ?? NewRow(descriptor), isNew, built.Message);
            }

            OperationResult saved = PortalServices.Modules.Save(descriptor, row, isNew);
            if (!saved.Succeeded)
            {
                return EditFailed(descriptor, row, isNew, saved.Message);
            }

            Audit(descriptor.EditFeature, (isNew ? "افزودن" : "ویرایش") + " — " + descriptor.TitleFa,
                descriptor.Table, Describe(descriptor, row, isNew));

            TempData["Flash"] = (isNew ? "سطر جدید ثبت شد: " : "تغییرات ثبت شد: ") + descriptor.TitleFa;
            return RedirectToAction("Index", new { code = descriptor.Code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(string code, string key)
        {
            ModuleDescriptor descriptor = Resolve(code);
            if (descriptor == null)
            {
                return HttpNotFound("ماژول خواسته‌شده وجود ندارد.");
            }
            if (descriptor.ReadOnly || !(CurrentSession != null && CurrentSession.CanEdit(descriptor.EditFeature)))
            {
                return Denied(descriptor.EditFeature);
            }

            IReadOnlyDictionary<string, string> parsed = ParseKey(descriptor, key);
            OperationResult result = PortalServices.Modules.Delete(descriptor, parsed);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index", new { code = descriptor.Code });
            }

            Audit(descriptor.EditFeature, "حذف — " + descriptor.TitleFa, descriptor.Table, key ?? string.Empty);
            TempData["Flash"] = "سطر حذف شد: " + descriptor.TitleFa;
            return RedirectToAction("Index", new { code = descriptor.Code });
        }

        /// <summary>CSV of the current filter, for the operator who needs it in Excel.</summary>
        public ActionResult Export(string code, string term)
        {
            ModuleDescriptor descriptor = Resolve(code);
            if (descriptor == null)
            {
                return HttpNotFound("ماژول خواسته‌شده وجود ندارد.");
            }
            if (!CanView(descriptor))
            {
                return Denied(descriptor.ViewFeature);
            }

            var builder = new StringBuilder();
            builder.AppendLine("\uFEFF" + JalaliText.CsvRow(descriptor.Fields.Select(f => f.LabelFa)));

            int page = 1;
            ModulePage slice;
            do
            {
                slice = PortalServices.Modules.Page(descriptor, term, page, 200);
                foreach (ModuleRow row in slice.Rows)
                {
                    builder.AppendLine(JalaliText.CsvRow(
                        descriptor.Fields.Select(f => JalaliText.ForExport(row[f.Column]))));
                }
                page++;
            }
            while (slice.Rows.Count == 200 && page <= 50);

            string name = "t5web-" + descriptor.Code + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", name);
        }

        // ------------------------------------------------------------------ helpers

        private ActionResult EditFailed(ModuleDescriptor descriptor, ModuleRow row, bool isNew, string message)
        {
            ViewBag.Error = message;
            ViewBag.Title = (isNew ? "افزودن — " : "ویرایش — ") + descriptor.TitleFa;
            return View(new ModuleEditModel
            {
                Descriptor = descriptor,
                Row = row,
                IsNew = isNew,
                Key = isNew ? string.Empty : descriptor.KeyFrom(row),
                CanDelete = false,
                FormAction = Url.Action("Edit", "Modules", new { code = descriptor.Code })
            });
        }

        /// <summary>
        /// Resolves a module and honours its kill switch: a disabled module behaves like
        /// a missing one, so no action can be reached by guessing the code.
        /// </summary>
        /// <summary>The first module of the registry whose feature this session may read.</summary>
        private ModuleDescriptor FirstVisible()
        {
            foreach (ModuleDescriptor candidate in ModuleRegistry.All())
            {
                if (ModuleSwitch.IsEnabled(candidate.Code) && CanView(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static ModuleDescriptor Resolve(string code)
        {
            ModuleDescriptor descriptor = ModuleRegistry.Find(code);
            if (descriptor == null)
            {
                return null;
            }
            return ModuleSwitch.IsEnabled(descriptor.Code) ? descriptor : null;
        }

        private bool CanView(ModuleDescriptor descriptor)
        {
            AdminSession session = CurrentSession;
            return session != null && session.CanView(descriptor.ViewFeature);
        }

        /// <summary>Modules the operator may see, grouped the way the rail groups them.</summary>
        private IReadOnlyList<ModuleDescriptor> ModulesFor(ModuleDescriptor current)
        {
            AdminSession session = CurrentSession;
            if (session == null)
            {
                return new List<ModuleDescriptor>();
            }
            return ModuleRegistry.ForGroup(current.GroupFa)
                .Where(m => session.CanView(m.ViewFeature))
                .ToList();
        }

        private static ModuleRow NewRow(ModuleDescriptor descriptor)
        {
            var row = new ModuleRow();
            foreach (ModuleField field in descriptor.Fields)
            {
                if (field.Kind == FieldKind.Boolean)
                {
                    row[field.Column] = false;
                }
            }
            return row;
        }

        private static IReadOnlyDictionary<string, string> ParseKey(ModuleDescriptor descriptor, string key)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = (key ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.None);
            int index = 0;
            foreach (ModuleField field in descriptor.Keys)
            {
                values[field.Column] = index < parts.Length ? parts[index] : string.Empty;
                index++;
            }
            return values;
        }

        private static string Describe(ModuleDescriptor descriptor, ModuleRow row, bool isNew)
        {
            var parts = new List<string>();
            foreach (ModuleField field in descriptor.Fields.Where(f => !f.IsKey).Take(4))
            {
                parts.Add(field.LabelFa + "=" + (Convert.ToString(row[field.Column]) ?? string.Empty));
            }
            return (isNew ? "new row: " : "row: ") + string.Join("; ", parts);
        }
    }

    /// <summary>Model of the module list screen.</summary>
    public sealed class ModuleIndexModel
    {
        public ModuleDescriptor Descriptor { get; set; }
        public ModulePage Page { get; set; }
        public IReadOnlyList<ModuleDescriptor> Groups { get; set; }
        public bool CanEdit { get; set; }

        /// <summary>False for keys-only link tables: they are added and deleted, not edited.</summary>
        public bool CanUpdate { get; set; }
    }

    /// <summary>Model of the module edit screen.</summary>
    public sealed class ModuleEditModel
    {
        public ModuleDescriptor Descriptor { get; set; }
        public ModuleRow Row { get; set; }
        public bool IsNew { get; set; }
        public string Key { get; set; }
        public bool CanDelete { get; set; }
        public string FormAction { get; set; }
    }
}
