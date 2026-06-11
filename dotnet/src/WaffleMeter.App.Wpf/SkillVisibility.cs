using WaffleMeter.App.Core;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The user's visible-skill-code set for join-panel badges (port of the React visibleSkillCodes prop +
/// its saveProps persistence). CSV under the "visibleSkillCodes" property key; defaults to every tracked
/// code. Ports the migration guard: if the catalog grew (&gt;100 defaults) but the saved set is tiny
/// (&lt;40), treat it as stale and reset to defaults.
/// </summary>
public sealed class SkillVisibility
{
    private const string Key = "visibleSkillCodes";

    private readonly PropertyHandler _props;
    public HashSet<int> Codes { get; private set; }

    public SkillVisibility(PropertyHandler props)
    {
        _props = props;
        Codes = Load();
    }

    public bool IsVisible(int code) => Codes.Contains(code);

    public void Set(int code, bool visible)
    {
        if (visible ? Codes.Add(code) : Codes.Remove(code))
        {
            Save();
        }
    }

    public void SetMany(IEnumerable<int> codes, bool visible)
    {
        bool changed = false;
        foreach (int code in codes)
        {
            changed |= visible ? Codes.Add(code) : Codes.Remove(code);
        }

        if (changed)
        {
            Save();
        }
    }

    private HashSet<int> Load()
    {
        string? raw = _props.GetProperty(Key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<int>(SkillCatalog.DefaultVisibleCodes);
        }

        var set = new HashSet<int>();
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int code) && code > 0)
            {
                set.Add(code);
            }
        }

        // Migration guard (React): catalog grew but saved set is tiny -> stale, reset to defaults.
        if (SkillCatalog.DefaultVisibleCodes.Count > 100 && set.Count < 40)
        {
            return new HashSet<int>(SkillCatalog.DefaultVisibleCodes);
        }

        return set;
    }

    private void Save() => _props.SetProperty(Key, string.Join(",", Codes));
}
