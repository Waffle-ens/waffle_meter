namespace WaffleMeter.App.Core;

/// <summary>
/// A user-defined recurring reminder: fires at <see cref="Hour"/>:<see cref="Minute"/> on the selected
/// <see cref="Days"/> (empty = every day). The whole list is persisted as one Base64(JSON) settings value
/// (see <see cref="CustomAlarmCodec"/>).
/// </summary>
public sealed record CustomAlarm
{
    public string Id { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public string Title { get; init; } = "알람";

    public int Hour { get; init; }

    public int Minute { get; init; }

    /// <summary>Days the alarm repeats on, as <see cref="DayOfWeek"/> ints (0 = Sunday .. 6 = Saturday).
    /// Empty = every day.</summary>
    public IReadOnlyList<int> Days { get; init; } = [];
}
