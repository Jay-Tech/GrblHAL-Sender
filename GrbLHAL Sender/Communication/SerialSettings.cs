using System.IO.Ports;

public class SerialSettings
{
    public string PortName { get; set; }
    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public int ReadTimeOut { get; set; } = 50;
    public int ReceivedBytesThreshold { get; set; } = 1;
    public int ReadBufferSize { get; set; } = 2048;
    public int WriteBufferSize { get; set; } = 4096;

    public SerialSettings(string portName)
    {
        PortName = portName;
    }
}