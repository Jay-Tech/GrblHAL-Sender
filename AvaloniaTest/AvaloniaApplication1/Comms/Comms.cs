using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AvaloniaApplication1.Comms
{
    public delegate void DataReceivedHandler(string data);

    public class Comms
    {
        public enum State
        {
            AwaitAck,
            DataReceived,
            ACK,
            NAK
        }

        public enum ResetMode
        {
            None,
            DTR,
            RTS
        }

        public enum StreamType
        {
            Serial,
            Telnet,
            Websocket
        }

        public const int TXBUFFERSIZE = 4096, RXBUFFERSIZE = 1024;

        public static StreamComms com = null;
    }

    public interface StreamComms
    {
        bool IsOpen { get; }
        int OutCount { get; }
        string Reply { get; }
        Comms.StreamType StreamType { get; }
        Comms.State CommandState { get; set; }
        bool EventMode { get; set; }
        Action<int> ByteReceived { get; set; }

        void Close();
        int ReadByte();
        void WriteByte(byte data);
        void WriteBytes(byte[] bytes, int len);
        void WriteString(string data);
        void WriteCommand(string command);
        string GetReply(string command);
        void AwaitAck();
        void AwaitAck(string command);
        void AwaitResponse(string command);
        void AwaitResponse();
        void PurgeQueue();

        event DataReceivedHandler DataReceived;
    }

}
