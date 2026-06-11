using System.Collections.ObjectModel;
using System.Windows;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Runtime style switcher. Each skin is a palette ResourceDictionary (Themes/Skin.&lt;Name&gt;.xaml) of
/// <c>Skin.*</c> brushes; the windows reference them via DynamicResource, so swapping the palette in
/// Application.Resources re-themes the whole app live. The palette is app-wide (brushes only) — the
/// settings window opts into Skin.Controls.xaml for its control look, deliberately keeping the
/// overlay/detail/color-picker custom looks untouched. Selection persists to the "skin" setting.
/// </summary>
public sealed class SkinManager
{
    public sealed record SkinOption(string Name, string Label);

    public static readonly IReadOnlyList<SkinOption> Skins = new[]
    {
        new SkinOption("dark", "다크"),
        new SkinOption("midnight", "미드나잇"),
        new SkinOption("slate", "슬레이트"),
        new SkinOption("light", "라이트"),
    };

    private readonly PropertyHandler _props;
    private ResourceDictionary? _palette;

    public SkinManager(PropertyHandler props) => _props = props;

    /// <summary>Raised after a skin is applied (lets light/dark-aware view models rebuild brushes).</summary>
    public event Action? Changed;

    /// <summary>True when the active skin is the light palette (drives light text-color overrides).</summary>
    public bool IsLight => Current == "light";

    public string Current
    {
        get
        {
            string? s = _props.GetProperty("skin");
            return Skins.Any(x => x.Name == s) ? s! : "dark";
        }
    }

    public void ApplyInitial() => Apply(Current);

    /// <summary>Apply the next skin in <see cref="Skins"/> (the overlay 테마 button). Returns its label.</summary>
    public string Cycle()
    {
        int index = 0;
        for (int i = 0; i < Skins.Count; i++)
        {
            if (Skins[i].Name == Current)
            {
                index = i;
                break;
            }
        }

        SkinOption next = Skins[(index + 1) % Skins.Count];
        Apply(next.Name);
        return next.Label;
    }

    public void Apply(string name)
    {
        if (!Skins.Any(x => x.Name == name))
        {
            name = "dark";
        }

        ResourceDictionary? next = TryLoad(name) ?? TryLoad("dark");
        if (next == null)
        {
            return; // could not load any palette — leave current resources untouched
        }

        Collection<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;
        if (_palette != null)
        {
            merged.Remove(_palette);
        }

        merged.Insert(0, next); // palette first; DynamicResource Skin.* resolves from here
        _palette = next;
        _props.SetProperty("skin", name);
        Changed?.Invoke();
    }

    private static ResourceDictionary? TryLoad(string name)
    {
        try
        {
            return new ResourceDictionary { Source = PaletteUri(name) };
        }
        catch
        {
            return null;
        }
    }

    private static Uri PaletteUri(string name)
    {
        string file = "Skin." + char.ToUpperInvariant(name[0]) + name[1..] + ".xaml";
        return new Uri("pack://application:,,,/Themes/" + file, UriKind.Absolute);
    }
}
