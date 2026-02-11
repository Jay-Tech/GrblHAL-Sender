using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Communication
{
    public class Tcp : ICommsAdapter
    {
        public event EventHandler<string>? OnDataReceived;
        public void WriteByte(byte data)
        {
            throw new NotImplementedException();
        }

        public void WriteCommand(string command)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public bool IsConnected { get; set; }
    }
}
