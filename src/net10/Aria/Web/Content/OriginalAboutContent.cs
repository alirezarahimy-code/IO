namespace Aria.Web.Content;

/// <summary>
/// Public, DB-independent informational labels from THIS product's SHA-pinned
/// TWF 4.28 MyTime3.Content.PersianString resources. The original About view
/// embeds a historical version banner and dynamically generated images; neither
/// is a verified new-product version or a safe offline asset, so neither is
/// silently copied here. The full original HTML/layout is NOT claimed equivalent.
/// Razor must HTML-encode these labels; never render them with Html.Raw.
/// </summary>
public static class OriginalAboutContent
{
    public const string Title = "درباره ما";
    public const string Company = "شرکت سامانه های بعد پنجم";
    public const string Description1 = "طراح و تولید کننده سیستم های الکترونیکی و کامپیوتری ";
    public const string Description2 = "ارائه کننده نسل جدید سیستم های حضور و غیاب و کنترل دسترسی";
    public const string Phone = "تلفن : 89350(021)";
    public const string Fax = "تلفکس : 44293581";
    public const string Copyright = "کلیه حقوق مادی معنوی این نرم افزار متعلق به شرکت سامانه های بعد پنجم می باشد و هر گونه کپی یا استفاده غیر مجاز از آن پیگرد قانونی دارد";

    // Explicit same-run golden map; no credentials, customer data, links,
    // unverified resource banners or raw HTML. This is not a product config.
    public static IReadOnlyDictionary<string, string> Labels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["About"] = Title,
            ["bode5"] = Company,
            ["bode5Descriptions1"] = Description1,
            ["bode5Descriptions2"] = Description2,
            ["bode5Tel"] = Phone,
            ["bode5Fac"] = Fax,
            ["bode5Copy"] = Copyright
        };
}
