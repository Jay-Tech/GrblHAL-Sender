using GrbLHALSender.Configuration;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

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

            // Serial — keep the stored port when nothing is selected. The ComboBox
            // clears its selection whenever the chosen port is missing from the list
            // (controller unplugged or mid-reset), and defaulting to COM1 there would
            // overwrite a good setting with a wrong one on the next Save.
            if (!string.IsNullOrWhiteSpace(SerialPort))
                config.SerialSettings.PortName = SerialPort;
            config.SerialSettings.BaudRate = BaudRate;

            // TCP
            config.TcpSettings.IpAddress = TcpIpAddress ?? "192.168.5.1";
            config.TcpSettings.PortNumber = TcpPort;

            // WebSocket
            config.WebSocketSettings.IpAddress = WsIpAddress ?? "192.168.5.1";
            config.WebSocketSettings.PortNumber = WsPort;
            _configManager.SaveConfig();
        }

        public void RefreshPorts() => ReconcilePorts(AvailablePorts, QueryPorts());

        /// <summary>
        /// Brings <paramref name="target"/> in line with <paramref name="desired"/> by
        /// adding and removing individual items, never clearing.
        /// <para>
        /// Clear()-then-refill drops the ComboBox's SelectedItem, and the two-way
        /// binding writes that null straight back into SerialPort — so a refresh could
        /// silently forget the chosen port. Reconciling also heals a list that already
        /// holds duplicate rows.
        /// </para>
        /// </summary>
        internal static void ReconcilePorts(IList<string> target, IList<string> desired)
        {
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(target[i]))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                if (i >= target.Count)
                    target.Add(desired[i]);
                else if (target[i] != desired[i])
                    target.Insert(i, desired[i]);
            }

            // Trailing leftovers: duplicates the loop above walked past.
            while (target.Count > desired.Count)
                target.RemoveAt(target.Count - 1);
        }

        private static List<string> QueryPorts()
        {
            try
            {
                return OrderPorts(System.IO.Ports.SerialPort.GetPortNames());
            }
            catch (Exception)
            {
                // Serial ports may not be available on all platforms
                return [];
            }
        }

        /// <summary>
        /// De-duplicates and orders the names the OS reports.
        /// <para>
        /// De-duplication matters on Windows: GetPortNames reads the
        /// HKLM\HARDWARE\DEVICEMAP\SERIALCOMM registry map, and a USB CDC device that
        /// re-enumerates (a controller reset or re-plug) can leave several device
        /// entries pointing at the same COM name until Windows cleans them up. Those
        /// arrive here as repeated identical names.
        /// </para>
        /// </summary>
        internal static List<string> OrderPorts(IEnumerable<string> names) =>
            names
                .Where(p => p != null)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                // Numeric ordering, so COM11 sorts after COM4 rather than before it.
                .OrderBy(PortPrefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(PortNumber)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string PortPrefix(string port)
        {
            int i = 0;
            while (i < port.Length && !char.IsDigit(port[i])) i++;
            return port[..i];
        }

        private static int PortNumber(string port)
        {
            int i = 0;
            while (i < port.Length && !char.IsDigit(port[i])) i++;
            return int.TryParse(port.AsSpan(i), out var number) ? number : int.MaxValue;
        }
    }
}
