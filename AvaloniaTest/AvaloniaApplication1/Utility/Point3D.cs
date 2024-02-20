using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApplication1.Utility
{
    public struct Point3D
    {
        internal double _x;
        internal double _y;
        internal double _z;

        /// <summary>Gets or sets the x-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</summary>
        /// <returns>The x-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</returns>
        public double X
        {
            get => this._x;
            set => this._x = value;
        }

        /// <summary>Gets or sets the y-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</summary>
        /// <returns>The y-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</returns>
        public double Y
        {
            get => this._y;
            set => this._y = value;
        }

        /// <summary>Gets or sets the z-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</summary>
        /// <returns>The z-coordinate of this <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</returns>
        public double Z
        {
            get => this._z;
            set => this._z = value;
        }
        /// <summary>Initializes a new instance of the <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</summary>
        /// <param name="x">The <see cref="P:System.Windows.Media.Media3D.Point3D.X" /> value of the new <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</param>
        /// <param name="y">The <see cref="P:System.Windows.Media.Media3D.Point3D.Y" /> value of the new <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</param>
        /// <param name="z">The <see cref="P:System.Windows.Media.Media3D.Point3D.Z" /> value of the new <see cref="T:System.Windows.Media.Media3D.Point3D" /> structure.</param>
        public Point3D(double x, double y, double z)
        {
            this._x = x;
            this._y = y;
            this._z = z;
        }
    }


}
