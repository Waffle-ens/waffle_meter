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
    };

    private readonly PropertyHandler _props;
    private ResourceDictionary? _palette;

    public SkinManager(PropertyHandler props) => _props = props;

    public string Current
    {
        get
        {
            string? s = _props.GetProperty("skin");
            return Skins.Any(x => x.Name == s) ? s! : "dark";
        }
    }

    public void ApplyInitial() => Apply(Current);

    public void Apply(string name)
    {
        if (!Skins.Any(x => x.Name == name))
        {
            name = "dark";
        }

        Collection<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = PaletteUri(name) };
        if (_palette != null)
        {
            merged.Remove(_palette);
        }

        merged.Insert(0, next); // palette first; DynamicResource Skin.* resolves from here
        _palette = next;
        _props.SetProperty("skin", name);
    }

    private static Uri PaletteUri(string name)
    {
        string file = "Skin." + char.ToUpperInvariant(name[0]) + name[1..] + ".xaml";
        return new Uri("pack://application:,,,/Themes/" + file, UriKind.Absolute);
    }
}
