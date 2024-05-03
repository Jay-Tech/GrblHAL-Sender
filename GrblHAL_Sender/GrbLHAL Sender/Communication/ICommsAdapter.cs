using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Communication
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
