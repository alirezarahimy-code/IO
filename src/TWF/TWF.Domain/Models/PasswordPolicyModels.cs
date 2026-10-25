namespace TWF.Domain.Models
{
    /// <summary>
    /// Represents the evaluation result of password rotation/force-change policies for a user.
    /// Fixes BUG-03 (ChangePassForce properly separated from ChangePassLimit) and BUG-07c.
    /// </summary>
    public sealed class PasswordCheckStatus
    {
        public bool RequiresImmediateChange { get; }
        public bool IsFirstLoginForceChange { get; }
        public bool IsPeriodicExpiryChange { get; }
        public string Reason { get; }
        public int DaysSinceLastChange { get; }
        public int DaysRemainingBeforeExpiry { get; }

        public PasswordCheckStatus(
            bool requiresImmediateChange,
            bool isFirstLoginForceChange,
            bool isPeriodicExpiryChange,
            string reason,
            int daysSinceLastChange,
            int daysRemainingBeforeExpiry)
        {
            RequiresImmediateChange = requiresImmediateChange;
            IsFirstLoginForceChange = isFirstLoginForceChange;
            IsPeriodicExpiryChange = isPeriodicExpiryChange;
            Reason = reason ?? string.Empty;
            DaysSinceLastChange = daysSinceLastChange;
            DaysRemainingBeforeExpiry = daysRemainingBeforeExpiry;
        }

        public static PasswordCheckStatus NoChangeRequired(int daysSinceLastChange = 0, int daysRemaining = int.MaxValue)
        {
            return new PasswordCheckStatus(false, false, false, string.Empty, daysSinceLastChange, daysRemaining);
        }
    }

    /// <summary>
    /// Result of validating and applying a password change.
    /// </summary>
    public sealed class PasswordChangeResult
    {
        public bool Succeeded { get; }
        public string Message { get; }

        public PasswordChangeResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public static PasswordChangeResult Success(string message) => new PasswordChangeResult(true, message);
        public static PasswordChangeResult Failure(string error) => new PasswordChangeResult(false, error);
    }
}
