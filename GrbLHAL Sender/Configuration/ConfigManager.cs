using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace GrbLHAL_Sender.Configuration
{
    public class ConfigManager
    {
        public event EventHandler<GHalSenderConfig> OnConfigLoaded;

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
            OnConfigLoaded?.Invoke(this, gHalSenderConfig);
            return GHalSenderConfig = gHalSenderConfig ?? new GHalSenderConfig();
            
        }
    }
}

public class GHalSenderConfig
{
    public bool UseMetric { get; set; } = true;
    public bool UseSerial { get; set; } = true;
    public bool AutoConnect { get; set; } = false;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public ToolList ToolList { get; set; } = new();
    public ObservableCollection<Macro> MacroList { get; set; } = new ObservableCollection<Macro>();
    public double[] JogDistance { get; set; } =
    [
        .01,
        1,
        10
    ];

    public double[] JogSpeed { get; set; } =
    [
        100,
        800,
        1500,
    ];
}
public class JogSpeedInformation
{
    public double Slow { get; set; } = 100;
    public double Fast { get; set; } = 800;
    public double Rapid { get; set; } = 1500;
    public JogSpeedInformation()
    {
        Slow = 100;
        Fast = 800;
        Rapid = 1500;
    }
}
public class JogDistanceInformation
{
    public double Fine { get; set; } = .001;
    public double Short { get; set; } = 1;
    public double Large { get; set; } = 10;

    public JogDistanceInformation()
    {
        Fine = .001;
        Short = 1;
        Large = 10;
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
        8,
        9,
    };
}

