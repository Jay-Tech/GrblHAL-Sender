using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Probe;
using GrbLHALSender.SdCard;
using GrbLHALSender.WebServer;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GrbLHALSender.Configuration;

public class GHalSenderConfig : ObservableObject
{
    private bool _useMetric;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConnectionType
    {
        Serial,
        Tcp,
        WebSocket
    }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConnectionType Connection { get; set; } = ConnectionType.Tcp;

    public bool Borderless
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool UseMetric
    {
        get => _useMetric;
        set => SetProperty(ref _useMetric, value);
    }


    public bool AutoConnect { get; set; } = false;

    public double PollRate { get; set; } = 200;
    public bool ShowToolpathProgress { get; set; } = true;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public TcpSettings TcpSettings { get; set; } = new(23, "192.168.5.1");
    public WebSocketSettings WebSocketSettings { get; set; } = new(81, "192.168.5.1");
    public AtcConfig AtcConfig { get; set; } = new();
    public GamepadConfig GamepadConfig { get; set; } = new();
    public WebServerConfig WebServerConfig { get; set; } = new();
    public ProbeConfig ProbeConfig { get; set; } = new();
    public SdCardConfig SdCardConfig { get; set; } = new();
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RendererType
    {
        Software,
        Hardware
    }
    public RendererType Renderer { get; set; } = RendererType.Software;
    public bool UseAntiAlias { get; set; } = true;
    public string SpindleImagePath { get; set; } = "spindle.png";
    public ToolList ToolList { get; set; } = new();
    public ObservableCollection<ViewModels.Macro> MacroList { get; set; } = new();
    public List<AuxOutputConfig> AuxOutputs { get; set; } = new();
    public double[] JogDistanceMetric { get; set; } =
    [
        .01,
        1,
        10
    ];

    public double[] JogSpeedMetric { get; set; } =
    [
        250,
        2500,
        5000,
    ];

    public double[] JogDistanceImperial { get; set; } =
    [
        .001,
        .01,
        1
    ];

    public double[] JogSpeedImperial { get; set; } =
    [
        10,
        150,
        300,
    ];
}