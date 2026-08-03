namespace COCOBOLOERPNEW.Helpers;

public static class ChartTextHelper
{
    public static string Safe(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"\u2067{value.Trim()}\u2069";

    public static string SafeClip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();

        if (maxLength > 0 && trimmed.Length > maxLength)
            trimmed = trimmed[..maxLength].TrimEnd() + "...";

        return Safe(trimmed);
    }
}
