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
    public ToolList ToolList { get; set; } = new();
    public ObservableCollection<ViewModels.Macro> MacroList { get; set; } = new ObservableCollection<ViewModels.Macro>();
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