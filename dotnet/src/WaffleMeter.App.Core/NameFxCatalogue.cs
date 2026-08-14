namespace WaffleMeter.App.Core;

/// <summary>
/// Which nickname-effect and gauge-skin ids this build can actually draw.
/// <para>The catalogue itself lives in the WPF layer because every entry owns brushes, so Core cannot ask it
/// directly — the dependency would run backwards. This carries the two questions Core needs answered.</para>
/// <para><b>Two predicates, not one.</b> A single "is this id in the catalogue" check is not symmetric: it lets a
/// gauge id sit in the nickname slot, where it resolves to a real brush and paints a bar-sized gradient across a
/// nickname.</para>
/// </summary>
/// <param name="IsKnownEffect">Accepts NICKNAME effect ids only.</param>
/// <param name="IsKnownGauge">Accepts GAUGE skin ids only.</param>
public sealed record NameFxCatalogue(Func<string, bool> IsKnownEffect, Func<string, bool> IsKnownGauge)
{
    /// <summary>Knows nothing — every grant is dropped. The default when no catalogue is supplied (tests,
    /// headless tools), because rendering an id we cannot draw is not an option and silently accepting one
    /// would hide the missing wiring until someone looked at a screen.</summary>
    public static readonly NameFxCatalogue None = new(_ => false, _ => false);
}
