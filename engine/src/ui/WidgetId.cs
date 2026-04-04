//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public readonly struct WidgetId(ulong value) : IEquatable<WidgetId>
{
    public static readonly WidgetId None = default;

    public readonly ulong Value = value;

    public static implicit operator ulong(WidgetId id) => id.Value;
    public static WidgetId operator +(WidgetId id, int index) => new(id.Value + (ulong)index);
    public static WidgetId operator +(WidgetId id, uint index) => new(id.Value + index);
    public static WidgetId operator ++(WidgetId id) => new(id.Value + 1);

    public bool IsNone => Value == 0;

    public bool Equals(WidgetId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WidgetId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
}
