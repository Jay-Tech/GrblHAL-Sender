using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Communication
{
    public class TcpSettings 
    {
       public int PortNumber { get; set; }
       public string IpAddress { get; set; }

       public TcpSettings()
       {
          
       }
        public TcpSettings(int portNumber, string ipAddress)
       {
           PortNumber = portNumber;
           IpAddress = ipAddress;
       }
    }
}
