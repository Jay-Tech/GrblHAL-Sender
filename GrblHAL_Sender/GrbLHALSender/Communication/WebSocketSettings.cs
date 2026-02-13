namespace GrbLHALSender.Communication
{
    public class WebSocketSettings
    {
        public int PortNumber { get; set; }
        public string IpAddress { get; set; }

        public WebSocketSettings()
        {

        }
        public WebSocketSettings(int portNumber, string ipAddress)
        {
            PortNumber = portNumber;
            IpAddress = ipAddress;
        }
    }
}
