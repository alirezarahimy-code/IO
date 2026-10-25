using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// Access levels for the admin portal.
    ///
    /// Policy (deliberately different from the legacy app, which took the last row
    /// written by whoever saved the form last):
    ///
    ///   1. Deny always wins, regardless of row order.
    ///   2. ReadOnly beats Deny-never: a person explicitly set to ReadOnly keeps read
    ///      access even if another row grants Allow for a *different* feature.
    ///   3. Having no row at all means Deny - the portal is closed by default.
    ///   4. PortalAdmin cannot be granted to somebody who has no UserInfo row with
    ///      WebUser = 1, because such a person cannot even sign in.
    /// </summary>
    public sealed class AccessService
    {
        private readonly IAdminRepository _repository;

        public AccessService(IAdminRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            _repository = repository;
        }

        /// <summary>Collects the effective level per feature, applying the deny-wins rule.</summary>
        public AdminSession BuildSession(PersonnelRow person, IEnumerable<PermissionRow> rows)
        {
            if (person == null)
            {
                throw new ArgumentNullException(nameof(person));
            }

            var session = new AdminSession
            {
                BadgeNumber = person.BadgeNumber,
                DisplayName = person.FullName,
                IssuedUtc = DateTime.UtcNow,
                TitleName = _repository.GetTitleName(person.BadgeNumber)
            };

            // Stable order so the result never depends on SQL row order.
            var ordered = (rows ?? Enumerable.Empty<PermissionRow>())
                .OrderBy(r => (int)r.Level)
                .ThenBy(r => (int)r.Feature);

            foreach (PermissionRow row in ordered)
            {
                session.Grant(row.Feature, row.Level);
            }
            return session;
        }

        public IReadOnlyList<PermissionRow> Get(long badgeNumber)
        {
            return _repository.GetPermissions(badgeNumber);
        }

        /// <summary>
        /// Changes one level. Refuses to leave the portal with zero administrators,
        /// which would lock everybody out after a well-meaning cleanup.
        /// </summary>
        public OperationResult Set(long badgeNumber, AdminFeature feature, AccessLevel level)
        {
            if (badgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی نامعتبر است.");
            }

            PersonnelRow person = _repository.GetPersonnel(badgeNumber);
            if (person == null)
            {
                return OperationResult.Fail("پرسنل با این شماره یافت نشد.");
            }

            if (feature == AdminFeature.PortalAdmin && level == AccessLevel.Allow)
            {
                if (!person.IsWebUser)
                {
                    return OperationResult.Fail("برای مدیر پورتال، ابتدا باید کاربر وب (WebUser) فعال شود.");
                }
            }

            if (feature == AdminFeature.PortalAdmin && level != AccessLevel.Allow)
            {
                bool otherAdminExists = _repository.GetPermissions(badgeNumber)
                    .Where(p => p.BadgeNumber != badgeNumber)
                    .Any(p => p.Feature == AdminFeature.PortalAdmin && p.Level == AccessLevel.Allow);

                if (!otherAdminExists && CountAdmins() <= 1)
                {
                    return OperationResult.Fail("حذف آخرین مدیر پورتال مجاز نیست؛ ابتدا مدیر دیگری تعیین کنید.");
                }
            }

            if (level == AccessLevel.ReadOnly &&
                AdminFeatures.RequiredLevelFor(feature) == AccessLevel.Allow)
            {
                // ReadOnly on an edit-only feature would create a right nobody can use.
                return OperationResult.Fail("این قابلیت فقط سطح «مجاز» یا «ممنوع» می‌پذیرد.");
            }

            return _repository.SetPermission(badgeNumber, feature, level);
        }

        private int CountAdmins()
        {
            // Reading every permission row is acceptable: the portal has tens of admins,
            // not thousands, and this runs only when revoking PortalAdmin.
            var all = _repository.GetPermissions(0);
            return all.Count(p => p.Feature == AdminFeature.PortalAdmin && p.Level == AccessLevel.Allow);
        }

        /// <summary>
        /// Menu model for the shell: only features the operator can actually reach.
        /// </summary>
        public IReadOnlyList<AdminFeature> VisibleFeatures(AdminSession session)
        {
            if (session == null)
            {
                return new List<AdminFeature>();
            }
            return AdminFeatures.All()
                .Where(session.CanView)
                .OrderBy(f => (int)f)
                .ToList();
        }
    }
}
