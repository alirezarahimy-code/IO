using System;
using System.Linq;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;
using TWF.Infrastructure.Localization;

namespace TWF.Application.Security
{
    /// <summary>
    /// Service for evaluating and enforcing TWF password policies:
    /// 1. ChangePassForce (mandatory password change on first login).
    /// 2. ChangePassMonthly &amp; ChangePassLimit (periodic password expiration every N days).
    /// 3. ChangePassRestriction (password complexity &amp; non-reuse validation).
    /// Replaces MyTime3.Controllers.ChangePassController and fixes BUG-03 and BUG-07c.
    /// </summary>
    public sealed class PasswordPolicyService
    {
        private readonly IT5BackendAdapter _backend;
        private readonly ITwfDataRepository _dataRepository;
        private readonly IAppSettingsService _settingsService;

        public PasswordPolicyService(
            IT5BackendAdapter backend,
            ITwfDataRepository dataRepository,
            IAppSettingsService settingsService)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        /// <summary>
        /// Evaluates whether the specified user must change their password immediately upon login.
        /// Replaces ChangePassController.ChangePassCheck / CheckPassForce / CheckPassMonthly.
        /// </summary>
        public PasswordCheckStatus EvaluatePasswordStatus(long badgeNumber, DateTime? referenceNow = null)
        {
            if (badgeNumber == 0)
            {
                // System admin is exempt from employee PassCheckProps rotation
                return PasswordCheckStatus.NoChangeRequired();
            }

            TwfAppSettings settings = _settingsService.GetSettings();
            DateTime now = (referenceNow ?? DateTime.Now).Date;
            PasswordChangeHistoryDto history = _dataRepository.GetPasswordChangeHistory(badgeNumber);

            // 1. Check first-login mandatory password change (ChangePassForce - fixed in BUG-03)
            if (settings.ChangePassForce)
            {
                if (history == null || !history.HasChangedPassword)
                {
                    return new PasswordCheckStatus(
                        requiresImmediateChange: true,
                        isFirstLoginForceChange: true,
                        isPeriodicExpiryChange: false,
                        reason: "بر اساس سیاست امنیتی سازمان، در اولین ورود باید رمز عبور خود را تغییر دهید.",
                        daysSinceLastChange: 0,
                        daysRemainingBeforeExpiry: 0);
                }
            }

            // 2. Check periodic password rotation (ChangePassMonthly + ChangePassLimit days)
            if (settings.ChangePassMonthly && settings.ChangePassLimit > 0)
            {
                if (history == null)
                {
                    return new PasswordCheckStatus(
                        requiresImmediateChange: true,
                        isFirstLoginForceChange: false,
                        isPeriodicExpiryChange: true,
                        reason: $"مهلت اعتبار رمز عبور شما ({settings.ChangePassLimit} روز) به پایان رسیده است.",
                        daysSinceLastChange: settings.ChangePassLimit,
                        daysRemainingBeforeExpiry: 0);
                }

                int daysElapsed = Math.Max(0, (int)(now - history.LastChangeDate.Date).TotalDays);
                int daysRemaining = settings.ChangePassLimit - daysElapsed;

                if (daysElapsed >= settings.ChangePassLimit)
                {
                    return new PasswordCheckStatus(
                        requiresImmediateChange: true,
                        isFirstLoginForceChange: false,
                        isPeriodicExpiryChange: true,
                        reason: $"مهلت اعتبار رمز عبور شما ({settings.ChangePassLimit} روز) به پایان رسیده است. لطفاً رمز جدیدی انتخاب کنید.",
                        daysSinceLastChange: daysElapsed,
                        daysRemainingBeforeExpiry: 0);
                }

                return PasswordCheckStatus.NoChangeRequired(daysElapsed, daysRemaining);
            }

            int elapsed = history != null ? Math.Max(0, (int)(now - history.LastChangeDate.Date).TotalDays) : 0;
            return PasswordCheckStatus.NoChangeRequired(elapsed);
        }

        /// <summary>
        /// Validates and changes a user's password in Personnel DB and records the change timestamp in [PassCheckProps].
        /// </summary>
        public PasswordChangeResult ChangePassword(
            long badgeNumber,
            string oldPassword,
            string newPassword,
            string confirmNewPassword,
            DateTime? changedAt = null)
        {
            if (string.IsNullOrEmpty(oldPassword))
                return PasswordChangeResult.Failure("رمز عبور فعلی را وارد نمایید.");

            if (string.IsNullOrEmpty(newPassword))
                return PasswordChangeResult.Failure("رمز عبور جدید نمی‌تواند خالی باشد.");

            if (!string.Equals(newPassword, confirmNewPassword, StringComparison.Ordinal))
                return PasswordChangeResult.Failure("رمز عبور جدید و تکرار آن مطابقت ندارند.");

            if (string.Equals(oldPassword, newPassword, StringComparison.Ordinal))
                return PasswordChangeResult.Failure("رمز عبور جدید نباید با رمز عبور فعلی یکسان باشد.");

            TwfAppSettings settings = _settingsService.GetSettings();
            if (settings.ChangePassRestriction)
            {
                var (isComplex, complexityError) = ValidatePasswordComplexity(newPassword, badgeNumber);
                if (!isComplex)
                    return PasswordChangeResult.Failure(complexityError);
            }

            bool updated;
            if (badgeNumber == 0)
            {
                updated = _backend.ChangeAdminPassword(oldPassword, newPassword);
            }
            else
            {
                updated = _backend.ChangeEmployeePassword(badgeNumber, oldPassword, newPassword);
            }

            if (!updated)
            {
                return PasswordChangeResult.Failure("رمز عبور فعلی نادرست است.");
            }

            if (badgeNumber > 0)
            {
                _dataRepository.RecordPasswordChange(badgeNumber, changedAt ?? DateTime.Now);
            }

            return PasswordChangeResult.Success(PersianString.SuccessOperation);
        }

        public static (bool IsValid, string ErrorMessage) ValidatePasswordComplexity(string password, long badgeNumber)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                return (false, "رمز عبور باید حداقل ۶ کاراکتر باشد.");
            }

            if (badgeNumber > 0 && password.Contains(badgeNumber.ToString()))
            {
                return (false, "رمز عبور نباید شامل شماره پرسنلی شما باشد.");
            }

            bool hasLetter = password.Any(char.IsLetter);
            bool hasDigit = password.Any(char.IsDigit);

            if (!hasLetter || !hasDigit)
            {
                return (false, "رمز عبور باید ترکیبی از حروف و اعداد باشد.");
            }

            return (true, string.Empty);
        }
    }
}
