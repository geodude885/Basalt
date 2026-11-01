using System.Globalization;

namespace Basalt.Services.Charts;

// Colours are 0f to 1f
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public static Rgba None => new(0f, 0f, 0f, 0f);

    /// "#RRGGBB" or "#RRGGBBAA" or "none"
    public static bool TryParse(string? text, out Rgba colour)
    {
        colour = default;
        if (string.Equals(text?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            colour = None;
            return true;
        }

        var hex = (text ?? "").Trim().TrimStart('#');
        if ((hex.Length != 6 && hex.Length != 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        if (hex.Length == 6) value = (value << 8) | 0xFF;
        colour = new Rgba(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }
}
