namespace SS14.Admin.Helpers;

public static class BanDurationAdjuster
{
    public static int AdjustMinutes(int currentMinutes, int deltaMinutes)
    {
        var adjustedMinutes = currentMinutes + deltaMinutes;
        return adjustedMinutes < 0 ? 0 : adjustedMinutes;
    }

    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes == 0)
            return "Permanent";

        var isNegative = totalMinutes < 0;
        var duration = TimeSpan.FromMinutes(Math.Abs(totalMinutes));
        var parts = new List<string>();

        if (duration.Days > 0)
            parts.Add(FormatPart(duration.Days, "day"));

        if (duration.Hours > 0)
            parts.Add(FormatPart(duration.Hours, "hour"));

        if (duration.Minutes > 0 || parts.Count == 0)
            parts.Add(FormatPart(duration.Minutes, "minute"));

        var formatted = string.Join(", ", parts);
        return isNegative ? $"-{formatted}" : formatted;
    }

    private static string FormatPart(int value, string unit)
    {
        var suffix = value == 1 ? string.Empty : "s";
        return $"{value} {unit}{suffix}";
    }
}
