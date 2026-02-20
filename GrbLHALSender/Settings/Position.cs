using System;

namespace GrbLHALSender.Settings;

public class Position : IEquatable<Position>
{
    public double MPos { get; set; }
    public double Wco { get; set; }

    public bool Equals(Position? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MPos == other.MPos && Wco == other.Wco;
    }

    public override bool Equals(object? obj) => Equals(obj as Position);

    public override int GetHashCode() => HashCode.Combine(MPos, Wco);
}
