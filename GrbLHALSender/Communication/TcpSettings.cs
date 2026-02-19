namespace GrbLHALSender.Communication
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
