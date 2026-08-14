using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>One line of the import preview.</summary>
/// <param name="Group">Section heading, in user language.</param>
/// <param name="Label">What the setting is called.</param>
/// <param name="From">Current value, formatted for display.</param>
/// <param name="To">Value the code would apply.</param>
public sealed record SettingsChange(string Group, string Label, string From, string To);

/// <summary>
/// What applying a code would actually do. Built before anything is written, so the user agrees to a specific
/// list rather than to the word "가져오기".
/// </summary>
public sealed class SettingsBundlePlan
{
    public required SettingsBundle Bundle { get; init; }

    /// <summary>Keys whose value would change, with before and after.</summary>
    public required IReadOnlyList<SettingsChange> Changes { get; init; }

    /// <summary>Keys already equal to the incoming value — carried, but nothing happens.</summary>
    public required int UnchangedCount { get; init; }

    /// <summary>Keys in the code that this build does not know. Ignored, and counted so the preview can say so
    /// instead of pretending the code applied whole.</summary>
    public required int UnknownCount { get; init; }

    /// <summary>Keys this build knows but the code omits. Local values stay — a code is not a reset.</summary>
    public required int MissingCount { get; init; }

    public bool HasWork => Changes.Count > 0;
}

/// <summary>
/// Turns settings into a code and back into a plan.
/// <para><b>Everything moves as the RAW stored string.</b> <c>PropertyHandler.GetProperty</c> re-decodes on the
/// way out, so exporting through it and importing through a model setter would put a value in memory that the
/// file never contains — right this session, different after a restart. Raw out, raw in, then
/// <see cref="MeterSettings.Reload"/>: each side keeps the representation it expects.</para>
/// </summary>
public static class SettingsBundleBuilder
{
    public static SettingsBundle Build(PropertyHandler props, SettingsProfile profile, string appVersion, DateTimeOffset now)
    {
        IReadOnlyDictionary<string, string> raw = props.RawEntries();
        var bundle = new SettingsBundle
        {
            Version = 1,
            Profile = SettingsBundleCodec.ProfileTag(profile),
            App = appVersion,
            CreatedAt = now.ToString("O"),
        };

        foreach (SettingsKey k in SettingsKeyCatalog.For(profile))
        {
            // A key never written is left out rather than exported as its default. Sending defaults would make
            // the code overwrite the receiver's deliberate choices with "whatever the sender never touched".
            if (raw.TryGetValue(k.Key, out string? v))
            {
                bundle.Data[k.Key] = v;
            }
        }

        return bundle;
    }

    /// <summary>Compare a decoded code against the current settings, without writing anything.</summary>
    public static SettingsBundlePlan Plan(PropertyHandler props, SettingsBundle bundle)
    {
        IReadOnlyDictionary<string, string> raw = props.RawEntries();
        var changes = new List<SettingsChange>();
        int unchanged = 0, unknown = 0;

        foreach ((string key, string value) in bundle.Data)
        {
            SettingsKey? known = SettingsKeyCatalog.Find(key);
            if (known is null)
            {
                // Either a key from a newer build, or one we have since retracted. Neither is worth failing the
                // whole import over; the preview reports the count.
                unknown++;
                continue;
            }

            string current = raw.GetValueOrDefault(key, string.Empty);
            if (string.Equals(current, value, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            changes.Add(new SettingsChange(known.Group, known.Label, Display(current), Display(value)));
        }

        SettingsProfile profile = SettingsBundleCodec.ParseProfile(bundle.Profile);
        int missing = SettingsKeyCatalog.For(profile).Count(k => !bundle.Data.ContainsKey(k.Key));

        return new SettingsBundlePlan
        {
            Bundle = bundle,
            Changes = changes,
            UnchangedCount = unchanged,
            UnknownCount = unknown,
            MissingCount = missing,
        };
    }

    /// <summary>Values are stored strings — long JSON blobs and CSV lists are common. Trim for the preview.</summary>
    private static string Display(string v)
    {
        if (v.Length == 0)
        {
            return "(없음)";
        }

        string oneLine = v.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= 42 ? oneLine : oneLine[..40] + "…";
    }
}
