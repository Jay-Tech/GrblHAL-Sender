using System;
using Avalonia.Xaml.Interactions.Custom;
using ReactiveUI;

namespace GrbLHALSender.Settings
{
    public class MachineSettings: ReactiveObject
    {
        private double _zSize;
        private double _ySize;
        private double _xSize;
        private bool _reportInMetric;

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
            set => this.RaiseAndSetIfChanged(ref _reportInMetric, value);
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
    }
}
