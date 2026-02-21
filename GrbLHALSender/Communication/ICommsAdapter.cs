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

        // --- Raw byte mode for binary protocols (YModem) ---

        /// <summary>Fires raw bytes when in raw mode (bypasses line-parsing).</summary>
        event EventHandler<byte[]>? OnRawDataReceived;

        /// <summary>Switch to raw byte mode — detaches line parser, fires OnRawDataReceived instead.</summary>
        void EnterRawMode();

        /// <summary>Restore normal line-parsing mode.</summary>
        void ExitRawMode();

        /// <summary>Write raw bytes directly (no \r appended, no queuing).</summary>
        void WriteBytes(byte[] data, int offset, int count);
    }
}
