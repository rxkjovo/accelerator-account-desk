using System.IO;
using System.Text.Json;

namespace AcceleratorDesk.App.Services;

public sealed class DeskSettings
{
    public string ClientExecutable { get; set; } = "";
}

public sealed class SettingsStore(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "settings.json");
    public DeskSettings Load() => File.Exists(_path)
        ? JsonSerializer.Deserialize<DeskSettings>(File.ReadAllText(_path)) ?? throw new InvalidDataException("设置文件内容为空。")
        : new DeskSettings();

    public void Save(DeskSettings settings)
    {
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }
}
