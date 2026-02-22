using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GrbLHALSender.Configuration;

public class ConfigManager
{
    public event EventHandler<GHalSenderConfig> OnConfigLoaded;
    public event EventHandler<GHalSenderConfig> OnConfigSaved;

    private readonly string _path = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string _fileName = "GHalSender_Config.json";
    public GHalSenderConfig? GHalSenderConfig { get; set; }

    public void SaveConfig()
    {
        var path = Path.Combine(_path, "Config");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        var fullPath = Path.Combine(path, _fileName);
        GHalSenderConfig ??= new GHalSenderConfig();
        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(GHalSenderConfig, options);
        File.WriteAllText(fullPath, jsonString);
        OnConfigSaved?.Invoke(this, GHalSenderConfig);
    }
    public GHalSenderConfig LoadConfig()
    {
        var fullPath = Path.Combine(_path, "Config", _fileName);
        if (!File.Exists(fullPath))
        {
            SaveConfig();
        }
        var readData = File.ReadAllText(fullPath);
        var gHalSenderConfig = JsonSerializer.Deserialize<GHalSenderConfig>(readData);
        GHalSenderConfig = gHalSenderConfig ?? new GHalSenderConfig();
        OnConfigLoaded?.Invoke(this, GHalSenderConfig);
        return GHalSenderConfig;

    }
}

public class ToolList
{
    public List<int> Tools { get; set; } = new()
    {
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8
    };
}