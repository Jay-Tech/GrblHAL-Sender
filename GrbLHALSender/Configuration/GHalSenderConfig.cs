using System.Collections.ObjectModel;
using GrbLHALSender.Communication;

namespace GrbLHALSender.Configuration;

public class GHalSenderConfig
{
    public enum ConnectionType
    {
        Serial,
        Tcp, 
        WebSocket
    }
    public ConnectionType Connection { get; set; } = ConnectionType.Tcp;
    public bool UseMetric { get; set; } = true;
    public bool AutoConnect { get; set; } = false;
    public SerialSettings SerialSettings { get; set; } = new("COM1");
    public TcpSettings TcpSettings { get; set; } = new(23, "192.168.5.1" );
    public WebSocketSettings WebSocketSettings { get; set; } = new(81, "192.168.5.1");
    public AtcConfig AtcConfig { get; set; } = new();
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
        100,
        800,
        1500,
    ];

    public double[] JogDistanceImperial { get; set; } =
    [
        .01,
        .5,
        1
    ];

    public double[] JogSpeedImperial { get; set; } =
    [
        100,
        200,
        400,
    ];
}