using System;
using Avalonia.Xaml.Interactions.Custom;
using ReactiveUI;

namespace GrbLHALSender.Settings
{
    public class MachineSettings : ReactiveObject
    {
        private double _zSize;
        private double _ySize;
        private double _xSize;
        private bool _reportInMetric;
        private double _xRapid;
        private double _yRapid;
        private double _zRapid;

        // grbl setting $130, 
        public double XSize
        {
            get => _xSize;
            set => this.RaiseAndSetIfChanged(ref _xSize, value);
        }
        //grbl setting$131
        public double YSize
        {
            get => _ySize;
            set => this.RaiseAndSetIfChanged(ref _ySize, value);
        }
        //grbl setting $132
        public double ZSize
        {
            get => _zSize;
            set => this.RaiseAndSetIfChanged(ref _zSize, value);
        }

        //grbl settings $13  report metric or inches
        public bool ReportInMetric
        {
            get => _reportInMetric;
            set
            {
                this.RaiseAndSetIfChanged(ref _reportInMetric, value);
                this.RaisePropertyChanged(nameof(UnitLabel));
            }
        }

        /// <summary>
        /// Returns "mm" or "in" based on the machine's $13 setting.
        /// </summary>
        public string UnitLabel => ReportInMetric ? "mm" : "in";

        //grbl settings $110  X rapid
        public double XRapid
        {
            get => _xRapid;
            set => this.RaiseAndSetIfChanged(ref _xRapid,value);
        }

        //grbl settings $111  Y rapid
        public double YRapid
        {
            get => _yRapid;
            set => this.RaiseAndSetIfChanged(ref _yRapid, value);
        }

        //grbl settings $112  Z rapid
        public double ZRapid
        {
            get => _zRapid;
            set => this.RaiseAndSetIfChanged(ref _zRapid, value);
        }

        public void SetIsMetric(string value)
        {
            ReportInMetric = value.Equals("0", StringComparison.InvariantCultureIgnoreCase);
        }
        public void SetXBoundaries(string value)
        {
            if (double.TryParse(value, out var size))
            {
                XSize = size;
            }

        }

        public void SetYBoundaries(string value)
        {
            if (double.TryParse(value, out var size))
            {
                YSize = size;
            }
        }
        public void SetZBoundaries(string value)
        {
            if (double.TryParse(value, out var size))
            {
                ZSize = size;
            }
        }
        public void SetXRapid(string value)
        {
            if (double.TryParse(value, out var rapid))
            {
                XRapid = rapid;
            }
        }

        public void SetYRapid(string value)
        {
            if (double.TryParse(value, out var rapid))
            {
                YRapid = rapid;
            }
        }

        public void SetZRapid(string value)
        {
            if (double.TryParse(value, out var rapid))
            {
                ZRapid = rapid;
            }
        }
    }
}
