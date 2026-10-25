using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Mvc;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// Offline personnel import.
    ///
    /// The legacy ImportFromExcelController depended on Excel Interop. Here the file is a
    /// UTF-8 CSV that is parsed, previewed and only then written; the confirm step
    /// re-validates every row from scratch, so a tampered hidden field cannot smuggle an
    /// invalid person past the screen.
    /// </summary>
    [AdminGate(AdminFeature.PersonnelImport)]
    public sealed class ImportController : PortalController
    {
        private const int MaxUploadBytes = 2 * 1024 * 1024;
        private const int MaxRows = 2000;

        public ActionResult Index()
        {
            ViewBag.Title = "ورود از فایل";
            return View(new ImportPreviewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Preview(HttpPostedFileBase file)
        {
            if (file == null || file.ContentLength == 0)
            {
                TempData["Flash"] = "فایلی انتخاب نشده است.";
                return RedirectToAction("Index");
            }
            if (file.ContentLength > MaxUploadBytes)
            {
                TempData["Flash"] = "حجم فایل بیش از ۲ مگابایت است.";
                return RedirectToAction("Index");
            }

            string extension = Path.GetExtension(file.FileName ?? string.Empty);
            if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Flash"] = "تنها فایل CSV پذیرفته می‌شود (خروجی «CSV UTF-8» از Excel).";
                return RedirectToAction("Index");
            }

            string text;
            using (var reader = new StreamReader(file.InputStream, new UTF8Encoding(false), true))
            {
                text = reader.ReadToEnd();
            }

            PersonnelImportService service = new PersonnelImportService(PortalServices.Repository);
            ImportPreview preview = service.Parse(text);

            var problems = new List<string>(preview.FileProblems);
            if (preview.Rows.Count > MaxRows)
            {
                problems.Add("فایل بیش از " + MaxRows + " سطر دارد؛ آن را به بخش‌های کوچک‌تر تقسیم کنید.");
            }

            var model = new ImportPreviewModel
            {
                Preview = preview,
                FileProblems = problems,
                Canonical = PersonnelImportService.ToCanonical(preview.Rows),
                FileName = Path.GetFileName(file.FileName ?? string.Empty)
            };
            ViewBag.Title = "پیش‌نمایش ورود از فایل";
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Commit(string canonical)
        {
            if (string.IsNullOrWhiteSpace(canonical))
            {
                TempData["Flash"] = "داده‌ای برای ثبت وجود ندارد.";
                return RedirectToAction("Index");
            }

            PersonnelImportService service = new PersonnelImportService(PortalServices.Repository);
            ImportOutcome outcome = service.Commit(canonical);

            if (outcome.Updated > 0)
            {
                Audit(AdminFeature.PersonnelImport, "ورود گروهی پرسنل", "فایل CSV",
                    outcome.SummaryFa + "; " + string.Join(" | ", outcome.Messages));
            }

            ViewBag.Title = "نتیجه ورود از فایل";
            return View("Result", new ImportResultModel
            {
                Outcome = outcome,
                CanCommit = false
            });
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    public sealed class ImportPreviewModel
    {
        public ImportPreview Preview { get; set; } = new ImportPreview();
        public IReadOnlyList<string> FileProblems { get; set; } = new List<string>();
        public string Canonical { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        public bool HasRows
        {
            get { return Preview != null && Preview.Rows.Count > 0; }
        }
    }

    public sealed class ImportResultModel
    {
        public ImportOutcome Outcome { get; set; }
        public bool CanCommit { get; set; }
    }
}
