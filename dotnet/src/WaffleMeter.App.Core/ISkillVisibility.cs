namespace WaffleMeter.App.Core;

/// <summary>A user-chosen set of skill codes, as the chip picker needs to see it. Two of these exist and they
/// must never be shared: the join panel's badge selection (<see cref="SkillVisibility"/>) and the cooldown
/// overlay's (<see cref="CooldownVisibility"/>). Both hand their backing set out by reference, so one instance
/// serving both windows would make toggling a join badge silently change what the cooldown overlay draws.</summary>
public interface ISkillVisibility
{
    bool IsVisible(int code);

    void Set(int code, bool visible);

    void SetMany(IEnumerable<int> codes, bool visible);
}
