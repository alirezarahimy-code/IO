using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The personnel photo archive - the desktop's frmPhoto / frmLastPhotos /
    /// frmPhotosReport, which the legacy web portal never had (docs/10, wave 1).
    ///
    /// Three deliberate decisions:
    ///   * read-only: the picture belongs to the terminal enrolment flow, so a web form
    ///     that could replace it would desynchronise the device cache;
    ///   * the list query only reads <c>DATALENGTH([Photo])</c>; the bytes are served one
    ///     person at a time by <see cref="Image"/>, so a 4,000-row grid stays a grid;
    ///   * the browser is told not to cache the image publicly - a photograph of a person
    ///     is personal data, not a logo.
    /// </summary>
    [AdminGate(AdminFeature.PersonnelPhoto, AllowReadOnly = true)]
    public sealed class PhotosController : PortalController
    {
        public ActionResult Index(string term, int? departmentId, bool missingOnly = false, int take = 60)
        {
            IPhotoStore store = PortalServices.Photos;
            if (store == null)
            {
                return NotConfigured("مخزن تصاویر تنظیم نشده است.");
            }

            try
            {
                var model = new PhotosIndexModel
                {
                    Term = term ?? string.Empty,
                    DepartmentId = departmentId,
                    MissingOnly = missingOnly,
                    Rows = store.Search(term, departmentId, missingOnly, take),
                    Departments = PortalServices.Departments
                        .BuildTree(PortalServices.Repository.GetDepartments())
                        .SelectMany(d => d.Flatten())
                        .OrderBy(d => d.DepartmentName, StringComparer.CurrentCulture)
                        .ToList(),
                    WithPhoto = store.CountWithPhoto(),
                    WithoutPhoto = store.CountWithoutPhoto()
                };
                ViewBag.Title = missingOnly ? "پرسنل بدون تصویر" : "تصاویر پرسنل";
                return View(model);
            }
            catch (Exception ex)
            {
                return NotConfigured("خواندن تصاویر ناموفق بود: " + ex.Message);
            }
        }

        /// <summary>
        /// Streams one picture. The identifier is the badge number, so the URL is
        /// predictable - which is fine, because the whole controller sits behind the
        /// PersonnelPhoto feature gate.
        /// </summary>
        public ActionResult Image(long id)
        {
            IPhotoStore store = PortalServices.Photos;
            if (store == null)
            {
                return HttpNotFound("مخزن تصاویر تنظیم نشده است.");
            }

            PhotoImage image = store.GetImage(id);
            if (image == null)
            {
                return HttpNotFound("برای این پرسنل تصویری ثبت نشده است.");
            }

            // Personal data: private to the browser session, revalidated, never a shared
            // proxy cache.
            Response.Cache.SetCacheability(HttpCacheability.Private);
            Response.Cache.SetMaxAge(TimeSpan.FromMinutes(10));
            Response.Cache.SetRevalidation(HttpCacheRevalidation.AllCaches);
            return File(image.Content, image.ContentType);
        }

        /// <summary>
        /// The printable sheet (frmPhotosReport): a grid of pictures an operator can put
        /// next to a terminal during enrolment.
        /// </summary>
        public ActionResult Sheet(string term, int? departmentId, bool missingOnly = false, int take = 60)
        {
            IPhotoStore store = PortalServices.Photos;
            if (store == null)
            {
                return NotConfigured("مخزن تصاویر تنظیم نشده است.");
            }

            var model = new PhotosIndexModel
            {
                Term = term ?? string.Empty,
                DepartmentId = departmentId,
                MissingOnly = missingOnly,
                Rows = store.Search(term, departmentId, missingOnly, take),
                Departments = new List<DepartmentNode>(),
                WithPhoto = store.CountWithPhoto(),
                WithoutPhoto = store.CountWithoutPhoto()
            };
            ViewBag.Title = "برگه تصاویر پرسنل";
            return View(model);
        }

        /// <summary>Readable page instead of a yellow screen when the store is missing.</summary>
        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    /// <summary>View model for the photo screens.</summary>
    /// <remarks>
    /// The department list is <see cref="DepartmentNode"/> (already flattened by the
    /// controller) because that is what the repository returns; the view only needs the
    /// id and the name.
    /// </remarks>
    public sealed class PhotosIndexModel
    {
        public string Term { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public bool MissingOnly { get; set; }
        public IReadOnlyList<PhotoRow> Rows { get; set; } = new List<PhotoRow>();
        public IReadOnlyList<DepartmentNode> Departments { get; set; } = new List<DepartmentNode>();
        public int WithPhoto { get; set; }
        public int WithoutPhoto { get; set; }

        public int Total
        {
            get { return WithPhoto + WithoutPhoto; }
        }

        public int CoveragePercent
        {
            get { return Total == 0 ? 0 : (int)Math.Round(WithPhoto * 100.0 / Total); }
        }
    }
}
