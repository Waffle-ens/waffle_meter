namespace WaffleMeter.Services;

/// <summary>Port of Kotlin <c>config.VersionConfig</c>: the app version from settings/version props.</summary>
public sealed record VersionConfig(string Version)
{
    public static VersionConfig LoadFromProperties(PropertyHandler properties) =>
        new(properties.GetProperty("version") ?? "1.6.9-dev");
}
