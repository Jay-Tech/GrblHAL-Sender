using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using GrbLHALSender.Configuration;
using ReactiveUI;

namespace GrbLHALSender.ViewModels
{
    public class ConnectionViewModel : ViewModelBase
    {
        public event Action? OnCloseRequested;

        private readonly ConfigManager _configManager;
        private int _selectedConnectionType;
        private string _serialPort;
        private int _baudRate;
        private string _tcpIpAddress;
        private int _tcpPort;
        private string _wsIpAddress;
        private int _wsPort;
        private bool _isAutoStart;
        private bool _showToolpathProgress;

        public ObservableCollection<string> ConnectionTypes { get; } = new()
        {
            "Serial",
            "TCP",
            "WebSocket"
        };

        public ObservableCollection<string> AvailablePorts { get; } = new();

        public ObservableCollection<int> BaudRates { get; } = new()
        {
            9600, 19200, 38400, 57600, 115200, 230400
        };

        public int SelectedConnectionType
        {
            get => _selectedConnectionType;
            set => this.RaiseAndSetIfChanged(ref _selectedConnectionType, value);
        }
        //Auto-start setting
        public bool IsAutoStart
        {
            get => _isAutoStart;
            set => this.RaiseAndSetIfChanged(ref _isAutoStart, value);
        }

        public bool ShowToolpathProgress
        {
            get => _showToolpathProgress;
            set => this.RaiseAndSetIfChanged(ref _showToolpathProgress, value);
        }

        // Serial settings
        public string SerialPort
        {
            get => _serialPort;
            set => this.RaiseAndSetIfChanged(ref _serialPort, value);
        }

        public int BaudRate
        {
            get => _baudRate;
            set => this.RaiseAndSetIfChanged(ref _baudRate, value);
        }

        // TCP settings
        public string TcpIpAddress
        {
            get => _tcpIpAddress;
            set => this.RaiseAndSetIfChanged(ref _tcpIpAddress, value);
        }

        public int TcpPort
        {
            get => _tcpPort;
            set => this.RaiseAndSetIfChanged(ref _tcpPort, value);
        }

        // WebSocket settings
        public string WsIpAddress
        {
            get => _wsIpAddress;
            set => this.RaiseAndSetIfChanged(ref _wsIpAddress, value);
        }

        public int WsPort
        {
            get => _wsPort;
            set => this.RaiseAndSetIfChanged(ref _wsPort, value);
        }
        public ICommand SaveConfigCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand CloseCommand { get; }

        public ConnectionViewModel(ConfigManager configManager)
        {
            _configManager = configManager;
            RefreshPortsCommand = ReactiveCommand.Create(RefreshPorts);
            SaveConfigCommand = ReactiveCommand.Create(SaveConfig);
            CloseCommand = ReactiveCommand.Create(() => OnCloseRequested?.Invoke());
            RefreshPorts();

        }

        private void SaveConfig()
        {
            if (_configManager?.GHalSenderConfig != null)
            {
                SaveToConfig(_configManager.GHalSenderConfig);
            }
        }

        public void LoadFromConfig(GHalSenderConfig config)
        {
            IsAutoStart = config.AutoConnect;
            ShowToolpathProgress = config.ShowToolpathProgress;
            // Set connection type index
            SelectedConnectionType = config.Connection switch
            {
                GHalSenderConfig.ConnectionType.Serial => 0,
                GHalSenderConfig.ConnectionType.Tcp => 1,
                GHalSenderConfig.ConnectionType.WebSocket => 2,
                _ => 1
            };

            // Serial
            SerialPort = config.SerialSettings.PortName;
            BaudRate = config.SerialSettings.BaudRate;

            // TCP
            TcpIpAddress = config.TcpSettings.IpAddress;
            TcpPort = config.TcpSettings.PortNumber;

            // WebSocket
            WsIpAddress = config.WebSocketSettings.IpAddress;
            WsPort = config.WebSocketSettings.PortNumber;
        }

        public void SaveToConfig(GHalSenderConfig config)
        {
            config.AutoConnect = IsAutoStart;
            config.ShowToolpathProgress = ShowToolpathProgress;
            config.Connection = SelectedConnectionType switch
            {
                0 => GHalSenderConfig.ConnectionType.Serial,
                1 => GHalSenderConfig.ConnectionType.Tcp,
                2 => GHalSenderConfig.ConnectionType.WebSocket,
                _ => config.Connection
            };

            // Serial
            config.SerialSettings.PortName = SerialPort ?? "COM1";
            config.SerialSettings.BaudRate = BaudRate;

            // TCP
            config.TcpSettings.IpAddress = TcpIpAddress ?? "192.168.5.1";
            config.TcpSettings.PortNumber = TcpPort;

            // WebSocket
            config.WebSocketSettings.IpAddress = WsIpAddress ?? "192.168.5.1";
            config.WebSocketSettings.PortNumber = WsPort;
            _configManager.SaveConfig();
        }

        public void RefreshPorts()
        {
            AvailablePorts.Clear();
            try
            {
                foreach (var port in System.IO.Ports.SerialPort.GetPortNames())
                {
                    AvailablePorts.Add(port);
                }
            }
            catch (Exception)
            {
                // Serial ports may not be available on all platforms
            }
        }
    }
}
