using System.Collections.ObjectModel;
using GrbLHAL_Sender.Configuration;

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