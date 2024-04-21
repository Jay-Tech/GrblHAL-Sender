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
        public GHalSenderConfig?  GHalSenderConfig { get;  set; }

        public void SaveConfig()
        {
            var path = Path.Combine(_path, "Config");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var fullPath = Path.Combine(path, _fileName);
            var ghalSender = new GHalSenderConfig();
            var options = new JsonSerializerOptions {WriteIndented = true };
            
            var jsonString = JsonSerializer.Serialize(ghalSender, options);
            File.WriteAllText(fullPath, jsonString);
        }
        public void LoadConfig()
        {
            var fullPath = Path.Combine(_path, "Config",_fileName);
            var readData = File.ReadAllText(fullPath);
            var gHalSenderConfig = JsonSerializer.Deserialize<GHalSenderConfig>(readData);
            GHalSenderConfig = gHalSenderConfig;

        }
    }
}

public class GHalSenderConfig
{
    public bool UseMetric { get; set; } = false;
    public JogInformation JogInformation { get; set; } = new();
}

public class JogInformation
{
    public int Slow { get; set; } 
    public int Fast { get; set; } 
    public int Rapid { get; set; }
    public int Fine { get; set; } 
    public int Short { get; set; }
    public int Large { get; set; }

    public JogInformation()
    {

    }
}