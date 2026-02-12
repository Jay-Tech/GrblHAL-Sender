using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using ReactiveUI;

namespace GrbLHAL_Sender.Settings
{
    public class MachineSettings: ReactiveObject
    {
        private double _zSize;
        private double _ySize;
        private double _xSize;
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
