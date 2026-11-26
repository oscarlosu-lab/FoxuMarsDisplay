namespace FoxuMarsDisplay.Core.Config;

/// <summary>
/// Computes effective display brightness based on an optional day/night schedule.
/// Supports both overnight windows (e.g. 22:00 to 07:00) and daytime windows (e.g. 13:00 to 17:00).
/// </summary>
public static class BrightnessSchedule
{
    public static int ComputeEffectiveBrightness(
        int baseBrightness,
        bool enabled,
        int startHour,
        int endHour,
        int nightBrightness,
        int currentHour)
    {
        int normal = Math.Clamp(baseBrightness, 0, 100);
        if (!enabled)
            return normal;

        int start = ((startHour % 24) + 24) % 24;
        int end = ((endHour % 24) + 24) % 24;
        int now = ((currentHour % 24) + 24) % 24;

        if (start == end)
            return normal;

        bool inNightWindow = start > end
            ? (now >= start || now < end)
            : (now >= start && now < end);

        return inNightWindow ? Math.Clamp(nightBrightness, 5, 100) : normal;
    }
}
