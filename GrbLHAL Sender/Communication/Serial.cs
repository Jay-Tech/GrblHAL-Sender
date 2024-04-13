using System;
using System.IO.Ports;using System.Runtime.InteropServices.ComTypes;

namespace GrbLHAL_Sender.Communication
{
    public class Serial
    {
        public Serial()
        {
           
        }

        public bool TryConnect(SerialSettings settings)
        {
            var serialPort = new SerialPort();
            serialPort.PortName = settings.PortName;
            serialPort.BaudRate = settings.BaudRate;
            serialPort.Parity = settings.Parity;
            serialPort.DataBits = settings.DataBits;
            serialPort.StopBits = settings.StopBits;
            serialPort.ReceivedBytesThreshold = settings.ReceivedBytesThreshold;
            serialPort.ReadTimeout = settings.ReadTimeOut;
            serialPort.ReadBufferSize = settings.ReadBufferSize;
            serialPort.WriteBufferSize = settings.WriteBufferSize;

            try
            {
                serialPort.Open();
                return serialPort.IsOpen;
            }
            catch (Exception e)
            {
                return false;
            }
        }
    }
}

public class SerialSettings()
{
    public string PortName { get; set; }
    public int BaudRate  { get; set; }
    public Parity Parity { get; set; }
    public int DataBits { get; set; }
    public StopBits StopBits { get; set; }
    public int ReadTimeOut { get; set; }
    public int ReceivedBytesThreshold { get; set; }
    public int ReadBufferSize { get; set; }
    public int WriteBufferSize { get; set; }
}