using System.IO;
using System.Text.Json;

namespace AcceleratorDesk.App.Services;

public sealed record RedemptionCodeEntry(string Code, string Note);

public sealed class RedemptionCodeStore(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "redemption-codes.json");
    private static readonly RedemptionCodeEntry[] ScreenshotCodes =
    [
        new("JSQPHB", "简书截图称 7 天；有效性与到账时长未验证。"),
        new("XL888", "简书截图称新用户 3 天；资格与有效性未验证。"),
        new("XL7878", "来自你提供的简书截图；有效性与到账时长未验证。"),
        new("XL1314", "来自你提供的简书截图；有效性与到账时长未验证。"),
        new("XL6789", "来自你提供的简书截图；有效性与到账时长未验证。"),
        new("XL8989", "来自你提供的简书截图；有效性与到账时长未验证。"),
        new("ws1997", "短视频截图称 7 天；有效性与到账时长未验证。")
    ];

    public IReadOnlyList<RedemptionCodeEntry> Load()
    {
        var result = ScreenshotCodes.ToList();
        foreach (var entry in ReadSaved())
            if (!result.Any(existing => string.Equals(existing.Code, entry.Code, StringComparison.OrdinalIgnoreCase))) result.Add(entry);
        return result;
    }

    public bool Add(string code)
    {
        code = code?.Trim() ?? "";
        if (!IsValid(code)) throw new ArgumentException("请填写 1～200 字符的完整兑换码，不要带空格或换行。");
        if (Load().Any(entry => string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))) return false;
        var saved = ReadSaved(); saved.Add(new(code, "本人添加；有效性与到账时长未验证。"));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }

    private List<RedemptionCodeEntry> ReadSaved()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var saved = JsonSerializer.Deserialize<List<RedemptionCodeEntry>>(File.ReadAllText(_path));
            if (saved is null || saved.Any(entry => entry is null || !IsValid(entry.Code) || entry.Note is null))
                throw new InvalidDataException("兑换码文件内容无效。原文件已保留，请先备份并检查后重试。");
            return saved;
        }
        catch (JsonException) { throw new InvalidDataException("兑换码文件无法读取。原文件已保留，请先备份并检查后重试。"); }
    }
    private static bool IsValid(string? code) => code is { Length: >= 1 and <= 200 } && !code.Any(char.IsWhiteSpace) && !code.Any(char.IsControl);
}
