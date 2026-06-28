using Model.Input;

namespace Model.Controls;

public readonly struct Shortcut(Key key, bool shift = false, bool ctrl = false, bool alt = false) : IEquatable<Shortcut>
{
#pragma warning disable CA2211
    public static Shortcut None = new(Key.None);
#pragma warning restore CA2211

    public Key Key { get; } = key;

    public bool Shift { get; } = shift;

    public bool Ctrl { get; } = ctrl;

    public bool Alt { get; } = alt;

    public static Shortcut Parse(string text)
    {
        var shortCutElements = text.Split(new[] { '|', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

        // Accept either "A" / "Enter" etc, and (optionally) "0".."9"
        var rawKey = shortCutElements.Length == 0 ? "" : shortCutElements[^1];

        if (TryParseKey(rawKey, out var key))
        {
            return new Shortcut(
                key,
                shift: shortCutElements.Contains("Shift", StringComparer.OrdinalIgnoreCase),
                ctrl: shortCutElements.Contains("Ctrl", StringComparer.OrdinalIgnoreCase),
                alt: shortCutElements.Contains("Alt", StringComparer.OrdinalIgnoreCase)
            );
        }

        return None;
    }

    private static bool TryParseKey(string raw, out Key key)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out key))
            return true;

        // Allow "0".."9" to map to D0..D9
        if (raw is [>= '0' and <= '9'])
        {
            key = Enum.Parse<Key>("D" + raw);
            return true;
        }

        key = Key.None;
        return false;
    }

    public override int GetHashCode()
    {
        return (Key, Shift, Ctrl, Alt).GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        return obj is Shortcut otherShortcut && otherShortcut.Equals(this);
    }

    public override string ToString()
    {
        var prefix = "";
        if (Ctrl) prefix += "Ctrl+";
        if (Alt) prefix += "Alt+";
        if (Shift) prefix += "Shift+";
        return prefix + Key;
    }

    public bool Equals(Shortcut other)
    {
        return Key == other.Key && Shift == other.Shift && Ctrl == other.Ctrl && Alt == other.Alt;
    }

    public static bool operator ==(Shortcut left, Shortcut right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Shortcut left, Shortcut right)
    {
        return !(left == right);
    }
}