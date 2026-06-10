using Velopack;
using Velopack.Sources;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// In-app auto-update via Velopack (replaces the Kotlin msiexec updater). Checks the GitHub release
/// feed, downloads the delta, and applies on demand. No-op when the app was not installed via Velopack
/// (e.g. a dev `dotnet run`), so it is safe to call unconditionally on startup.
/// </summary>
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/Waffle-ens/waffle_meter";

    private readonly UpdateManager? _manager;

    public UpdateService(bool prerelease)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease));
        }
        catch
        {
            _manager = null; // not a Velopack install
        }
    }

    /// <summary>Check + download the latest update; reports status. Applied on next launch.</summary>
    public async Task CheckAndDownloadAsync(Action<string>? status = null)
    {
        if (_manager is not { IsInstalled: true })
        {
            return;
        }

        try
        {
            UpdateInfo? update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                return;
            }

            status?.Invoke($"업데이트 다운로드 중… {update.TargetFullRelease.Version}");
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            status?.Invoke($"업데이트 준비됨 — 재시작 시 적용 ({update.TargetFullRelease.Version})");
            _pending = update;
        }
        catch (Exception ex)
        {
            status?.Invoke($"업데이트 확인 실패: {ex.Message}");
        }
    }

    private UpdateInfo? _pending;

    /// <summary>Apply a downloaded update and restart (call from a user "지금 재시작" action).</summary>
    public void ApplyAndRestart()
    {
        if (_manager is { IsInstalled: true } && _pending != null)
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
    }
}
