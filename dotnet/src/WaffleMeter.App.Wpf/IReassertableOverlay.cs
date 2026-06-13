namespace WaffleMeter.App.Wpf;

/// <summary>
/// A secondary game-overlay window (party-join / battle-history / skill-flyout / update-toast panels, and
/// the per-row detail window) that can re-claim its topmost when borderless-fullscreen AION2 re-asserts its
/// own topmost above it (e.g. on alt-tab return). The <see cref="OverlayController"/> poll drives this for
/// every registered overlay alongside the meter window, so an overlay left open across an alt-tab no longer
/// stays buried behind the game. Implementations must keep the re-claim a no-op when already on top and never
/// steal the game's foreground (NOACTIVATE).
/// </summary>
public interface IReassertableOverlay
{
    void ReassertTopmostIfBuried();
}
