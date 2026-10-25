using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>One device command that the portal is allowed to issue.</summary>
    public sealed class DeviceCommand
    {
        public string Code { get; set; } = string.Empty;
        public string TitleFa { get; set; } = string.Empty;
        public string NoteFa { get; set; } = string.Empty;

        /// <summary>True when the device answers with data the engine must import.</summary>
        public bool FetchesData { get; set; }
    }

    /// <summary>
    /// The device operations the portal keeps from the legacy
    /// DeviceOperationsController (37 actions, docs/03).
    ///
    /// Two things are deliberately gone:
    ///   * every lock command (ChangeLock, USBManagement, licence reset) - the rewrite has
    ///     no lock or licence dependency at all, so a command that pokes one cannot exist;
    ///   * free-form commands. The legacy screen let an operator post a raw command string,
    ///     which is the same as handing out the device's protocol.
    ///
    /// What remains is a closed set, each entry audited with who/when/which device.
    /// </summary>
    public sealed class DeviceCommandService
    {
        private static readonly List<DeviceCommand> Catalog = new List<DeviceCommand>
        {
            new DeviceCommand
            {
                Code = "sync-users",
                TitleFa = "همگام‌سازی پرسنل",
                NoteFa = "ارسال فهرست پرسنل فعال به دستگاه.",
                FetchesData = false
            },
            new DeviceCommand
            {
                Code = "sync-time",
                TitleFa = "تنظیم ساعت دستگاه",
                NoteFa = "ساعت دستگاه با ساعت سرور هم‌تراز می‌شود.",
                FetchesData = false
            },
            new DeviceCommand
            {
                Code = "pull-punches",
                TitleFa = "دریافت ترددها",
                NoteFa = "خواندن ترددهای ثبت‌شده روی دستگاه.",
                FetchesData = true
            },
            new DeviceCommand
            {
                Code = "pull-users",
                TitleFa = "دریافت فهرست پرسنل",
                NoteFa = "خواندن کاربران ثبت‌شده روی دستگاه (برای مقایسه).",
                FetchesData = true
            },
            new DeviceCommand
            {
                Code = "clear-punches",
                TitleFa = "پاک‌سازی ترددهای دستگاه",
                NoteFa = "فقط پس از دریافت کامل ترددها مجاز است؛ در دفتر رخداد ثبت می‌شود.",
                FetchesData = false
            }
        };

        public IReadOnlyList<DeviceCommand> Available()
        {
            return Catalog;
        }

        /// <summary>
        /// Validates a command request. The caller must have <see cref="AdminFeature.DeviceSync"/>;
        /// this method only enforces the shape of the request so the tests can assert it.
        /// </summary>
        public OperationResult Validate(string code, DeviceRow device, out DeviceCommand command)
        {
            command = null;
            if (device == null)
            {
                return OperationResult.Fail("دستگاه انتخاب نشده است.");
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                return OperationResult.Fail("دستور انتخاب نشده است.");
            }

            command = Catalog.FirstOrDefault(c =>
                string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                // A free-form or lock command lands here; the message is deliberately plain.
                return OperationResult.Fail("این دستور در پورتال مجاز نیست.");
            }
            if (device.MachineNumber <= 0)
            {
                return OperationResult.Fail("شماره دستگاه معتبر نیست.");
            }
            if (string.Equals(device.Ip, "0.0.0.0", StringComparison.Ordinal) && device.ConnectionType == 0)
            {
                return OperationResult.Fail("آدرس شبکه دستگاه تنظیم نشده است.");
            }
            return OperationResult.Ok();
        }
    }
}
