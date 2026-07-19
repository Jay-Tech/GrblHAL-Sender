using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GrbLHALSender.Configuration;

public class ConfigManager
{
    public event EventHandler<GHalSenderConfig> OnConfigLoaded;
    public event EventHandler<GHalSenderConfig> OnConfigSaved;

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GrblHAL-Sender");
    private readonly string _fileName = "GHalSender_Config.json";
    public GHalSenderConfig? GHalSenderConfig { get; set; }

    public void SaveConfig()
    {
        var path = Path.Combine(_path, "Config");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            CopyDefaultAssets(path);
        }

        var fullPath = Path.Combine(path, _fileName);
        var tempPath = fullPath + ".tmp";

        GHalSenderConfig ??= new GHalSenderConfig();
        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(GHalSenderConfig, options);

        // Atomic write: temp file → fsync → rename. Guarantees the on-disk file
        // is always either the previous complete version or the new complete
        // version, never a truncated mix — even if the process is killed
        // mid-save (systemd SIGTERM, power loss, OS shutdown race).
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(jsonString);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, fullPath, overwrite: true);

        OnConfigSaved?.Invoke(this, GHalSenderConfig);
    }

    /// <summary>
    /// Copies default asset files (e.g. Spindle.png) from the application
    /// directory into the Config folder on first run.
    /// </summary>
    private static void CopyDefaultAssets(string configDir)
    {
        var assets = new[] { "Spindle.png" };
        var appDir = AppContext.BaseDirectory;

        foreach (var asset in assets)
        {
            var destPath = Path.Combine(configDir, asset.ToLowerInvariant());
            if (File.Exists(destPath)) continue;

            // Look in Assets subfolder first, then app root
            var sourcePath = Path.Combine(appDir, "Assets", asset);
            if (!File.Exists(sourcePath))
                sourcePath = Path.Combine(appDir, asset);
            if (!File.Exists(sourcePath)) continue;

            File.Copy(sourcePath, destPath);
        }
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