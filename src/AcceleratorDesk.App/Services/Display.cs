using System.Globalization;

namespace AcceleratorDesk.App.Services;

public static class Display
{
    public const string DatePattern = "yyyy-MM-dd HH:mm";
    public static string Date(DateTimeOffset? value) => value?.ToLocalTime().ToString(DatePattern, CultureInfo.InvariantCulture) ?? "未记录";
    public static string DateInput(DateTimeOffset? value) => value?.ToLocalTime().ToString(DatePattern, CultureInfo.InvariantCulture) ?? "";
    public static DateTimeOffset? ParseDate(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTime.TryParseExact(text.Trim(), DatePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException($"{label}请填写 {DatePattern}，或留空。例：2026-11-30 20:00");
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new ArgumentException($"{label}处于本地夏令时切换的无效时间，请调整。");
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
