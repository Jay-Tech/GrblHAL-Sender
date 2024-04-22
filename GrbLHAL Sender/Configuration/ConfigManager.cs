using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GrbLHAL_Sender.Configuration
{
    public class ConfigManager
    {
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
            var ghalSender = new GHalSenderConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(ghalSender, options);
            File.WriteAllText(fullPath, jsonString);
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
            return GHalSenderConfig = gHalSenderConfig ?? new GHalSenderConfig();
        }
    }
}

public class GHalSenderConfig
{
    public bool UseMetric { get; set; } = false;
    public bool UseSerial { get; set; } = true;
    public bool AutoConnect { get; set; } = false;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public JogInformation JogInformation { get; set; } = new();
    public ToolList ToolList { get; set; } = new();
}

public class JogInformation
{
    public double Slow { get; set; } = 100;
    public double Fast { get; set; } = 800;
    public double Rapid { get; set; } = 1500;
    public double Fine { get; set; } = .001;
    public double Short { get; set; } = 1;
    public double Large { get; set; } = 10;

    public JogInformation()
    {

    }
}
public class ToolList
{
    public List<Tool> Tools { get; set; } = new()
    {
        new(1),
        new(2),
        new(3),
        new(4),
        new(5),
        new(6),
        new(7),

    };
}

public struct Tool
{
    public int ToolNumber { get; set; }
    public Tool(int number)
    {
        ToolNumber = number;
    }
}
