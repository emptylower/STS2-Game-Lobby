using System.IO;
using System.Text.Json;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectConfigPersistence
{
    public static LanConnectConfigData Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LanConnectConfigData>(json) ?? new LanConnectConfigData();
    }

    public static void Save(string path, LanConnectConfigData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}
