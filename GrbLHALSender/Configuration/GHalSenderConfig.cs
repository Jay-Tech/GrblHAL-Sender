using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Gpio;
using GrbLHALSender.Probe;
using GrbLHALSender.SdCard;
using GrbLHALSender.Theming;
using GrbLHALSender.WebServer;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using GrbLHALSender.Pendant;

namespace GrbLHALSender.Configuration;

public class GHalSenderConfig : ObservableObject
{
    private bool _useMetric;
    private bool _shutDownOs;

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

    public bool ShutDownOs
    {
        get => _shutDownOs;
        set => SetProperty(ref _shutDownOs, value);
    }

    public bool AutoConnect { get; set; } = false;
    public double PollRate { get; set; } = 200;
    public bool ShowToolpathProgress { get; set; } = true;

    // When true (default), the streamer pre-fills grblHAL's RX buffer
    // (character-counting protocol) for maximum throughput. When false,
    // only one line is in flight at a time — the next line is sent only
    // after the previous one is ack'd. This trades throughput for a
    // closer match between the highlighted gcode line and the actual
    // machine position, useful for slow jobs or visual debugging.
    public bool StreamBufferAhead { get; set; } = true;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public TcpSettings TcpSettings { get; set; } = new(23, "192.168.5.1");
    public WebSocketSettings WebSocketSettings { get; set; } = new(81, "192.168.5.1");
    public AtcConfig AtcConfig { get; set; } = new();
    public GamepadConfig GamepadConfig { get; set; } = new();
    public GpioConfig Gpio { get; set; } = new();
    public WebServerConfig WebServerConfig { get; set; } = new();
    public PendantConfig PendantConfig { get; set; } = new();
    public ProbeConfig ProbeConfig { get; set; } = new();
    public SdCardConfig SdCardConfig { get; set; } = new();
    public ThemeConfig Theme { get; set; } = new();
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RendererType
    {
        Software,
        Hardware
    }
    public RendererType Renderer { get; set; } = RendererType.Hardware;
    public bool UseAntiAlias { get; set; } = true;
    public string SpindleImagePath { get; set; } = "spindle.png";
    public ToolList ToolList { get; set; } = new();
    public ObservableCollection<ViewModels.Macro> MacroList { get; set; } = new();
    public List<AuxOutputConfig> AuxOutputs { get; set; } = new();

    /// <summary>
    /// User-defined pre/post command rules keyed off G-code events (homing, tool
    /// change, or any command the user types in). Empty by default — nothing is
    /// injected until the user adds a rule.
    /// </summary>
    public List<GcodeEventHook> GcodeEvents { get; set; } = new();
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