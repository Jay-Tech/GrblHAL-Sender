using System;

namespace GrbLHALSender.Communication
{
    public interface ICommsAdapter
    {
        event EventHandler<string> OnDataReceived;
        void WriteByte(byte data);
        void WriteCommand(string command);
        void Close();
        bool IsConnected { get;  set; }
    }
}
