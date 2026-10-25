using System;
using System.Collections.Concurrent;

namespace TWF.Infrastructure.Security
{
    /// <summary>
    /// Thread-safe brute-force login tracker.
    /// Tracks consecutive failed login attempts per username/client key to trigger dynamic Captcha
    /// or temporary cool-down lockouts in 100% offline environments.
    /// </summary>
    public sealed class LoginAttemptThrottle
    {
        private sealed class AttemptEntry
        {
            public int FailedCount;
            public DateTime LastFailedAtUtc;
            public DateTime? LockedUntilUtc;
        }

        private readonly ConcurrentDictionary<string, AttemptEntry> _entries =
            new ConcurrentDictionary<string, AttemptEntry>(StringComparer.OrdinalIgnoreCase);

        public int CaptchaThreshold { get; }
        public int LockoutThreshold { get; }
        public TimeSpan LockoutDuration { get; }
        public TimeSpan ResetWindow { get; }

        public LoginAttemptThrottle(
            int captchaThreshold = 3,
            int lockoutThreshold = 7,
            TimeSpan? lockoutDuration = null,
            TimeSpan? resetWindow = null)
        {
            CaptchaThreshold = Math.Max(1, captchaThreshold);
            LockoutThreshold = Math.Max(CaptchaThreshold + 1, lockoutThreshold);
            LockoutDuration = lockoutDuration ?? TimeSpan.FromMinutes(5);
            ResetWindow = resetWindow ?? TimeSpan.FromMinutes(15);
        }

        public void RecordFailure(string key, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            string normalizedKey = key.Trim();
            _entries.AddOrUpdate(
                normalizedKey,
                _ => new AttemptEntry
                {
                    FailedCount = 1,
                    LastFailedAtUtc = utcNow,
                    LockedUntilUtc = null
                },
                (_, existing) =>
                {
                    lock (existing)
                    {
                        if ((utcNow - existing.LastFailedAtUtc) > ResetWindow)
                        {
                            existing.FailedCount = 0;
                            existing.LockedUntilUtc = null;
                        }

                        existing.FailedCount++;
                        existing.LastFailedAtUtc = utcNow;
                        if (existing.FailedCount >= LockoutThreshold)
                        {
                            existing.LockedUntilUtc = utcNow.Add(LockoutDuration);
                        }
                        return existing;
                    }
                });
        }

        public void RecordSuccess(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _entries.TryRemove(key.Trim(), out _);
        }

        public bool RequiresCaptcha(string key, bool globalCaptchaSetting, DateTime utcNow)
        {
            if (globalCaptchaSetting)
                return true;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!_entries.TryGetValue(key.Trim(), out AttemptEntry entry))
                return false;

            lock (entry)
            {
                if ((utcNow - entry.LastFailedAtUtc) > ResetWindow)
                    return false;

                return entry.FailedCount >= CaptchaThreshold;
            }
        }

        public bool IsLockedOut(string key, DateTime utcNow, out TimeSpan remainingLockout)
        {
            remainingLockout = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!_entries.TryGetValue(key.Trim(), out AttemptEntry entry))
                return false;

            lock (entry)
            {
                if (entry.LockedUntilUtc.HasValue && entry.LockedUntilUtc.Value > utcNow)
                {
                    remainingLockout = entry.LockedUntilUtc.Value - utcNow;
                    return true;
                }
                return false;
            }
        }
    }
}
