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
            set { this.RaiseAndSetIfChanged(ref _xSize, value); this.RaisePropertyChanged(nameof(DisplayXSize)); }
        }
        //grbl setting$131
        public double YSize
        {
            get => _ySize;
            set { this.RaiseAndSetIfChanged(ref _ySize, value); this.RaisePropertyChanged(nameof(DisplayYSize)); }
        }
        //grbl setting $132
        public double ZSize
        {
            get => _zSize;
            set { this.RaiseAndSetIfChanged(ref _zSize, value); this.RaisePropertyChanged(nameof(DisplayZSize)); }
        }

        private const double MmToInch = 25.4;

        //grbl settings $13  report metric or inches
        public bool ReportInMetric
        {
            get => _reportInMetric;
            set
            {
                this.RaiseAndSetIfChanged(ref _reportInMetric, value);
                this.RaisePropertyChanged(nameof(UnitLabel));
                RaiseDisplayPropertiesChanged();
            }
        }

        /// <summary>
        /// Returns "mm" or "in" based on the machine's $13 setting.
        /// </summary>
        public string UnitLabel => ReportInMetric ? "mm" : "in";

        // Display-unit properties: returns values in the display unit (mm or inches)
        // Machine settings $130/$131/$132 and $110/$111/$112 are always stored in mm.
        // When $13=1 (imperial), these convert mm → inches for rendering/display.
        public double DisplayXSize => ReportInMetric ? XSize : XSize / MmToInch;
        public double DisplayYSize => ReportInMetric ? YSize : YSize / MmToInch;
        public double DisplayZSize => ReportInMetric ? ZSize : ZSize / MmToInch;
        public double DisplayXRapid => ReportInMetric ? XRapid : XRapid / MmToInch;
        public double DisplayYRapid => ReportInMetric ? YRapid : YRapid / MmToInch;
        public double DisplayZRapid => ReportInMetric ? ZRapid : ZRapid / MmToInch;

        //grbl settings $110  X rapid
        public double XRapid
        {
            get => _xRapid;
            set { this.RaiseAndSetIfChanged(ref _xRapid, value); this.RaisePropertyChanged(nameof(DisplayXRapid)); }
        }

        //grbl settings $111  Y rapid
        public double YRapid
        {
            get => _yRapid;
            set { this.RaiseAndSetIfChanged(ref _yRapid, value); this.RaisePropertyChanged(nameof(DisplayYRapid)); }
        }

        //grbl settings $112  Z rapid
        public double ZRapid
        {
            get => _zRapid;
            set { this.RaiseAndSetIfChanged(ref _zRapid, value); this.RaisePropertyChanged(nameof(DisplayZRapid)); }
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

        private void RaiseDisplayPropertiesChanged()
        {
            this.RaisePropertyChanged(nameof(DisplayXSize));
            this.RaisePropertyChanged(nameof(DisplayYSize));
            this.RaisePropertyChanged(nameof(DisplayZSize));
            this.RaisePropertyChanged(nameof(DisplayXRapid));
            this.RaisePropertyChanged(nameof(DisplayYRapid));
            this.RaisePropertyChanged(nameof(DisplayZRapid));
        }
    }
}
