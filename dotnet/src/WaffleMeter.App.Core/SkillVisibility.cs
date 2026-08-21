using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>
/// The user's visible-skill set for the join-panel skill badges, persisted across restarts.
///
/// <para><b>The file stores the complement.</b> <c>joinSkills.hidden</c> holds the codes the user turned
/// OFF, not the ones they kept. Storing the kept set looks more natural and is a trap, because the default
/// is "everything visible" and that default has to survive three things a visible-list cannot express:</para>
/// <list type="bullet">
///   <item>An empty selection. A kept-list serialises "nothing selected" as "", which on the way back in is
///   indistinguishable from "this user has never opened the picker" — so 전체 해제 came back as 전체 선택.
///   As a hidden-list, empty means "nothing hidden", which is exactly what an absent key means too: the two
///   readings agree, and the ambiguity disappears instead of being papered over with a sentinel.</item>
///   <item>A small selection. Nothing about a short hidden-list is suspicious, so there is no temptation to
///   add the size heuristic that caused this bug — see the history note below.</item>
///   <item>A catalogue that grows. Skills added by a later patch are simply not in anyone's hidden-list, so
///   they show up for everybody automatically. A kept-list would need a companion "codes known when saved"
///   key and a set-difference on every load to reach the same place.</item>
/// </list>
///
/// <para><b>History — why the key was renamed.</b> Through v2.10.3 this was <c>visibleSkillCodes</c>, a kept
/// list carrying a heuristic ported from the React build: "catalogue has &gt;100 codes but the saved set has
/// &lt;40 → the save is stale, reset to defaults". The threshold was sized against a catalogue of ~22 codes and
/// was never re-derived; once the catalogue passed 100 the left half became permanently true and the guard
/// degenerated to "throw away any selection under 40 codes". Job groups in the picker are 17–20 skills, so
/// even keeping two whole jobs (max 39) was silently discarded on every startup and the user got all 167 back.
/// Worse, the reset was not written out, so the good selection sat on disk until the next toggle overwrote it.
/// The rename is not cosmetic: the value's meaning is inverted, so an older build must not read it. Old builds
/// skip unknown keys on import (<see cref="SettingsKeyCatalog.IsKnown"/>), which they now do for this one.</para>
///
/// <para><b>Migration.</b> A leftover <c>visibleSkillCodes</c> is converted once, without the size guard, so a
/// selection the guard had been ignoring is restored rather than lost — then the legacy key is deleted, which
/// is also what makes the migration one-shot. Two shapes of that key exist in the wild: the 2.x CSV, and the
/// pre-2.0 build's <c>JSON.stringify</c> array, which landed in this same file under this same name.</para>
///
/// <para><b>Known limit of the conversion.</b> A kept-list cannot say anything about codes that did not exist
/// when it was written, so converting one marks every later catalogue addition as hidden. A list saved before
/// 권성 shipped therefore hides 권성. Nothing on disk dates the list, so any other reading would be a guess;
/// the effect is one-time, and the picker's per-job 전체 선택 undoes it in a click. Only the conversion has
/// this problem — once stored as a hidden-list, new skills appear on their own, which is the point.</para>
/// </summary>
public sealed class SkillVisibility
{
    private const string Key = "joinSkills.hidden";

    /// <summary>Pre-2.10.4 key, opposite meaning (codes KEPT). Read once, then removed.</summary>
    private const string LegacyKey = "visibleSkillCodes";

    private readonly PropertyHandler _props;

    /// <summary>The visible codes. Handed out by reference to <c>JoinRequestViewModel</c> and the picker, so it
    /// is always mutated in place and never reassigned. Only the storage format is the complement — everything
    /// in memory speaks "visible".</summary>
    public HashSet<int> Codes { get; }

    public SkillVisibility(PropertyHandler props)
    {
        _props = props;
        Codes = new HashSet<int>();
        LoadInto(Codes);
    }

    /// <summary>
    /// Raised when the whole set was replaced from outside (a settings import). Views that snapshot the set at
    /// construction — <c>SkillSettingsViewModel</c> builds its rows once — would otherwise keep drawing the old
    /// state and write it back the moment the user touched a toggle.
    /// </summary>
    public event Action? Changed;

    /// <summary>Re-read through the <see cref="PropertyHandler"/> — which is the authority, not the file behind
    /// it: an import writes into this same handler and then calls here. The set is updated IN PLACE: the same HashSet instance was
    /// handed to <c>JoinRequestViewModel</c> and <c>SkillSettingsViewModel</c>, so replacing it would leave
    /// them holding the old one.</summary>
    public void Reload()
    {
        var fresh = new HashSet<int>();
        LoadInto(fresh);
        Codes.Clear();
        Codes.UnionWith(fresh);
        Changed?.Invoke();
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

    private void LoadInto(HashSet<int> target)
    {
        // Presence, not emptiness. "" is a real answer — pre-2.10.4 wrote it for 전체 해제 — and treating it as
        // "never configured" is the very conflation this class exists to undo. Only an absent key is unset.
        string? legacy = _props.GetProperty(LegacyKey);
        if (legacy is not null)
        {
            // Codes the catalogue has since dropped are discarded here rather than carried as dead weight.
            HashSet<int> kept = Parse(legacy);
            foreach (int code in SkillCatalog.DefaultVisibleCodes)
            {
                if (kept.Contains(code))
                {
                    target.Add(code);
                }
            }

            // A value that named codes but none we recognise is not a choice, it is a value we failed to read
            // — an older format, or a file edited by hand. Persisting the complement of "nothing" would write
            // "hide all 167" and delete the evidence in the same breath, leaving a blank picker that looks
            // deliberate. Fall back to the default and still drop the key, so this cannot run twice.
            bool unreadable = kept.Count > 0 && target.Count == 0;
            if (unreadable)
            {
                target.UnionWith(SkillCatalog.DefaultVisibleCodes);
            }

            // One file write, not two. Dropping the old key and writing the new one are separate saves
            // otherwise, and a crash in between would leave a file with neither — i.e. the selection we are
            // here to rescue, lost at the moment of rescuing it.
            _props.RunBatched(() =>
            {
                _props.RemoveProperty(LegacyKey);
                if (!unreadable)
                {
                    SaveFrom(target);
                }
            });
            return;
        }

        HashSet<int> hidden = Parse(_props.GetProperty(Key));
        foreach (int code in SkillCatalog.DefaultVisibleCodes)
        {
            if (!hidden.Contains(code))
            {
                target.Add(code);
            }
        }
    }

    /// <summary>
    /// Comma-separated codes. Brackets are trimmed because the pre-2.0 build wrote this same key into this
    /// same file as <c>JSON.stringify(codes)</c> — "[11800000,11750000]" — so a v1.x upgrade's value arrives
    /// bracketed. Splitting on ',' alone would silently drop exactly the first and last entry, and a
    /// one-or-two-skill selection would parse to nothing at all.
    /// </summary>
    private static HashSet<int> Parse(string? raw)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        raw = raw.Trim().TrimStart('[').TrimEnd(']');

        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int code) && code > 0)
            {
                set.Add(code);
            }
        }

        return set;
    }

    private void Save() => SaveFrom(Codes);

    /// <summary>Writes the complement. Iterating the catalogue (not the set) keeps the line order stable, so
    /// the properties file diffs cleanly instead of reshuffling on every toggle.</summary>
    private void SaveFrom(HashSet<int> visible) =>
        _props.SetProperty(Key, string.Join(",", SkillCatalog.DefaultVisibleCodes.Where(c => !visible.Contains(c))));
}
