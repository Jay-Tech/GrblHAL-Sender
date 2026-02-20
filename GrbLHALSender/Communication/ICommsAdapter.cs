using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GrbLHALSender.Communication
{
    public interface ICommsAdapter: INotifyPropertyChanged
    {
        event EventHandler<string> OnDataReceived;
        void WriteByte(byte data);
        void WriteCommand(string command);
        void Close();
        bool IsConnected { get; set; }
    }
}
