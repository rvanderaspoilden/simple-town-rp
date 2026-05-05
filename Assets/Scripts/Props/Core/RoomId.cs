using System;

/// <summary>
/// Lightweight, value-type room identifier.
/// Rooms are logical groupings — completely independent of Unity scenes.
/// A scene can hold multiple rooms (e.g. city + a shop backroom).
/// A room can span multiple scenes (not typical, but supported).
/// </summary>
public readonly struct RoomId : IEquatable<RoomId> {
    public readonly string Value;

    public RoomId(string value) {
        Value = value ?? string.Empty;
    }

    public static readonly RoomId None = new RoomId(string.Empty);

    // ── Factory helpers ───────────────────────────────────────────────────────

    public static RoomId City(string name = "city") => new RoomId(name);

    public static RoomId Apartment(string street, int doorNumber) =>
        new RoomId($"apartment:{street}:{doorNumber}");

    public static RoomId Custom(string id) => new RoomId(id);

    // ── Value semantics ───────────────────────────────────────────────────────

    public bool IsValid => !string.IsNullOrEmpty(Value);

    public bool Equals(RoomId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is RoomId other && Equals(other);

    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    public static bool operator ==(RoomId a, RoomId b) => a.Equals(b);

    public static bool operator !=(RoomId a, RoomId b) => !a.Equals(b);

    public override string ToString() => Value;
}
