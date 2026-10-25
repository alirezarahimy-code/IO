using System;

namespace T5Web.Application.Services
{
    /// <summary>How this installation reaches the attendance devices.</summary>
    public enum DeviceTransportKind
    {
        /// <summary>Nobody does. Commands are refused instead of queued into a void.</summary>
        None = 0,

        /// <summary>
        /// The vendor's own device product (Link5 / Comm5Service) is running on the server
        /// and exposes its device operations over HTTP. The portal posts a command to it.
        /// </summary>
        Link5 = 1,

        /// <summary>
        /// A queue the portal writes and some agent outside this repository drains.
        /// Only valid when such an agent is actually deployed.
        /// </summary>
        Queue = 2
    }

    /// <summary>
    /// Where device commands go, read from one appSetting.
    ///
    /// The rewrite shipped a command screen that wrote a row into [T5WebDeviceCommand]
    /// and told the operator the device would pick it up. Nothing drained that queue:
    /// the legacy console posted to Link5 (Link5ServiceURL, 127.0.0.1:15222/Device/*),
    /// and the rewrite kept the button but not the wire (BUG-44). A button that reports
    /// success and changes nothing is worse than a button that refuses, so the transport
    /// is now explicit configuration:
    ///
    ///   T5WebDeviceTransport = none | link5:&lt;base url&gt; | queue
    ///
    /// "none" is the default. An unconfigured installation says so on the screen and
    /// refuses the command, which is the truth.
    /// </summary>
    public sealed class DeviceTransport
    {
        public const string Key = "T5WebDeviceTransport";

        /// <summary>The legacy console's own value, kept because Link5 still serves it.</summary>
        public const string LegacyDefaultUrl = "http://127.0.0.1:15222/Device/";

        private DeviceTransport(DeviceTransportKind kind, string endpoint, string raw)
        {
            Kind = kind;
            Endpoint = endpoint ?? string.Empty;
            Raw = raw ?? string.Empty;
        }

        public DeviceTransportKind Kind { get; }

        /// <summary>Base URL for <see cref="DeviceTransportKind.Link5"/>; empty otherwise.</summary>
        public string Endpoint { get; }

        /// <summary>The configuration value this was parsed from, for the audit line.</summary>
        public string Raw { get; }

        /// <summary>
        /// True when a command has somewhere real to go. "queue" counts only because the
        /// operator asserted an agent exists by configuring it.
        /// </summary>
        public bool IsConfigured => Kind != DeviceTransportKind.None;

        /// <summary>What the screen tells the operator about this installation.</summary>
        public string DescribeFa
        {
            get
            {
                switch (Kind)
                {
                    case DeviceTransportKind.Link5:
                        return "ارتباط با دستگاه از طریق سرویس Link5 (" + Endpoint + ")";
                    case DeviceTransportKind.Queue:
                        return "ارتباط با دستگاه از طریق صف [T5WebDeviceCommand] و عاملی که خارج از پورتال آن را برمی‌دارد";
                    default:
                        return "ارتباط با دستگاه در این نصب تنظیم نشده است؛ دستورها ثبت نمی‌شوند";
                }
            }
        }

        /// <summary>
        /// Parses the appSetting. Anything unusable is <see cref="DeviceTransportKind.None"/>:
        /// a typo must disable the feature, never silently pick a transport.
        /// </summary>
        public static DeviceTransport Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new DeviceTransport(DeviceTransportKind.None, null, raw);
            }

            string value = raw.Trim();
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return new DeviceTransport(DeviceTransportKind.None, null, raw);
            }
            if (value.Equals("queue", StringComparison.OrdinalIgnoreCase))
            {
                return new DeviceTransport(DeviceTransportKind.Queue, null, raw);
            }

            int colon = value.IndexOf(':');
            string scheme = colon < 0 ? value : value.Substring(0, colon);
            if (scheme.Equals("link5", StringComparison.OrdinalIgnoreCase))
            {
                string url = colon < 0 ? string.Empty : value.Substring(colon + 1).Trim();
                if (url.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                {
                    url = LegacyDefaultUrl;
                }
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute) ||
                    !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    // link5 without a usable base URL is not a transport.
                    return new DeviceTransport(DeviceTransportKind.None, url, raw);
                }
                if (!url.EndsWith("/", StringComparison.Ordinal))
                {
                    url += "/";
                }
                return new DeviceTransport(DeviceTransportKind.Link5, url, raw);
            }

            return new DeviceTransport(DeviceTransportKind.None, null, raw);
        }

        /// <summary>The URL one catalogue command would be posted to, or null.</summary>
        public string CommandUrl(string commandCode)
        {
            if (Kind != DeviceTransportKind.Link5 || string.IsNullOrWhiteSpace(commandCode))
            {
                return null;
            }
            // The legacy console's shape: {base}{operation}?machineNumber={n}. The operation
            // names come from the closed catalogue, never from the request (docs/15).
            return Endpoint + LegacyOperation(commandCode);
        }

        /// <summary>
        /// Catalogue code -> the operation name the device service uses. These names were
        /// read out of the legacy console's own assembly (T5V3Web.dll), not invented.
        /// </summary>
        public static string LegacyOperation(string code)
        {
            switch ((code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sync-users":
                    return "uploadUsers";
                case "pull-users":
                    return "downloadUsers";
                case "pull-punches":
                    return "downloadrecords";
                case "sync-time":
                    return "timesyncdata";
                case "clear-punches":
                    return "deleteallrecords";
                default:
                    return null;
            }
        }
    }
}
