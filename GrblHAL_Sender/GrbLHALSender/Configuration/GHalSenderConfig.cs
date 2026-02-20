using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Probe;
using GrbLHALSender.WebServer;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GrbLHALSender.Configuration;

public class GHalSenderConfig : ObservableObject
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConnectionType
    {
        Serial,
        Tcp,
        WebSocket
    }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConnectionType Connection { get; set; } = ConnectionType.Tcp;

    public bool UseMetric
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool AutoConnect { get; set; } = false;
    public bool ShowToolpathProgress { get; set; } = true;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public TcpSettings TcpSettings { get; set; } = new(23, "192.168.5.1");
    public WebSocketSettings WebSocketSettings { get; set; } = new(81, "192.168.5.1");
    public AtcConfig AtcConfig { get; set; } = new();
    public GamepadConfig GamepadConfig { get; set; } = new();
    public WebServerConfig WebServerConfig { get; set; } = new();
    public ProbeConfig ProbeConfig { get; set; } = new();
    public ToolList ToolList { get; set; } = new();
    public ObservableCollection<ViewModels.Macro> MacroList { get; set; } = new();
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