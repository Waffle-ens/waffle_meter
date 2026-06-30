using System.Collections.Concurrent;
using System.Diagnostics;
using WaffleMeter.Data;
using WaffleMeter.Services;

namespace WaffleMeter.Stats;

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsUploadQueue</c>: consent-gated, boss-only, kill-confirmed
/// upload of finished battles on a single background worker, with battle-hash de-duplication and
/// running counters. Wired to <see cref="DpsCalculator.OnBattleLogged"/> in the live app.
///
/// The work dispatcher and the kill-recheck delay are injected so the queue can run synchronously
/// (and instantly) under test; by default it owns a daemon thread and waits 4s before re-checking a
/// not-yet-confirmed kill (the boss may die just after the report is cut).
/// </summary>
public sealed class StatsUploadQueue : IDisposable
{
    private readonly StatsConsentManager _consent;
    private readonly StatsPayloadBuilder _builder;
    private readonly StatsApiClient _api;
    private readonly DataManager _data;
    private readonly PropertyHandler _props;
    private readonly Action<Action> _dispatch;
    private readonly Action _killRecheckDelay;
    private readonly Func<long> _clock;

    private readonly HashSet<string> _uploadedHashes = new();
    private readonly object _hashLock = new();

    private int _pending;
    private int _uploaded;
    private int _skipped;
    private int _failed;
    private long _lastUpdatedAt;
    private volatile string _clientVersion = "dev";
    private volatile string? _lastPath;
    private volatile string? _lastReason;

    private readonly BlockingCollection<Action>? _queue;
    private readonly Thread? _worker;

    public StatsUploadQueue(
        StatsConsentManager consent,
        StatsPayloadBuilder builder,
        StatsApiClient api,
        DataManager data,
        PropertyHandler props,
        Action<Action>? dispatch = null,
        Action? killRecheckDelay = null,
        Func<long>? clock = null)
    {
        _consent = consent;
        _builder = builder;
        _api = api;
        _data = data;
        _props = props;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _killRecheckDelay = killRecheckDelay ?? (() => Thread.Sleep(4_000));

        if (dispatch != null)
        {
            _dispatch = dispatch;
        }
        else
        {
            _queue = new BlockingCollection<Action>();
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "stats-upload-queue" };
            _worker.Start();
            _dispatch = job =>
            {
                if (!_queue.IsAddingCompleted)
                {
                    _queue.Add(job);
                }
            };
        }
    }

    private void WorkerLoop()
    {
        foreach (Action job in _queue!.GetConsumingEnumerable())
        {
            try
            {
                job();
            }
            catch
            {
                // a single upload failure must not kill the worker
            }
        }
    }

    public void Configure(string version) => _clientVersion = version;

    public void OfferIfEligible(DpsLog log)
    {
        if (!_consent.IsUploadAllowed())
        {
            MarkSkipped("consent_not_allowed");
            return;
        }

        MobInfo? target = log.Report.Target;
        if (target == null || !target.Mob.Boss || target.Mob.IsDummy)
        {
            MarkSkipped("not_boss");
            return;
        }

        if (IsKillConfirmed(log))
        {
            Enqueue(log, killConfirmed: true);
            return;
        }

        _dispatch(() =>
        {
            Interlocked.Increment(ref _pending);
            try
            {
                _killRecheckDelay();
                if (IsKillConfirmed(log))
                {
                    UploadPayload(log, killConfirmed: true);
                }
                else
                {
                    MarkSkipped("not_kill");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        });
    }

    public StatsUploadStatus Status() => new(
        Enabled: _consent.IsUploadAllowed(),
        Pending: Volatile.Read(ref _pending),
        Uploaded: Volatile.Read(ref _uploaded),
        Skipped: Volatile.Read(ref _skipped),
        Failed: Volatile.Read(ref _failed),
        LastPath: _lastPath,
        LastReason: _lastReason,
        LastUpdatedAt: Interlocked.Read(ref _lastUpdatedAt));

    public string OpenFolder()
    {
        string dir = Path.Combine(_props.AppDirectory(), "stats-upload");
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch
        {
            // best effort
        }

        return dir;
    }

    private void Enqueue(DpsLog log, bool killConfirmed)
    {
        _dispatch(() =>
        {
            Interlocked.Increment(ref _pending);
            try
            {
                UploadPayload(log, killConfirmed);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        });
    }

    private void UploadPayload(DpsLog log, bool killConfirmed)
    {
        switch (_builder.Build(log, _clientVersion, killConfirmed))
        {
            case BuildResult.Skip skip:
                MarkSkipped(skip.Reason);
                break;
            case BuildResult.Payload built:
                StatsUploadPayload payload = built.Value;
                lock (_hashLock)
                {
                    if (_uploadedHashes.Contains(payload.BattleHash))
                    {
                        MarkSkipped("duplicate");
                        return;
                    }
                }

                try
                {
                    ReportUploadResponse response = _api.PostReport(payload, _clientVersion);
                    lock (_hashLock)
                    {
                        _uploadedHashes.Add(payload.BattleHash);
                    }

                    // The signed upload earned/confirmed this install's grant for the uploader character —
                    // cache it so the "공개" toggle unlocks without waiting for a consent round-trip (§2.2).
                    if (response.Granted)
                    {
                        _consent.MarkGranted(payload.Character.IdentityHash);
                    }

                    Interlocked.Increment(ref _uploaded);
                    string reason = response.Duplicate ? "uploaded_duplicate" : "uploaded";
                    UpdateLast(_api.ReportEndpoint(), $"{reason}:{response.ReportId ?? "no_report_id"}");
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref _failed);
                    UpdateLast(_api.ReportEndpoint(), $"upload_failed:{Summarize(e)}");
                }

                break;
        }
    }

    private bool IsKillConfirmed(DpsLog log)
    {
        MobInfo? target = log.Report.Target;
        if (target == null)
        {
            return false;
        }

        bool snapshotKill = target.MaxHp > 0 && target.RemainHp <= 0;
        int? latestHp = _data.MobHp(target.Id);
        int latestMaxHp = _data.MobMaxHp(target.Id) ?? target.MaxHp;
        bool latestKill = latestMaxHp > 0 && latestHp == 0;
        return snapshotKill || latestKill;
    }

    private void MarkSkipped(string reason)
    {
        Interlocked.Increment(ref _skipped);
        UpdateLast(null, reason);
    }

    private void UpdateLast(string? path, string reason)
    {
        _lastPath = path;
        _lastReason = reason;
        Interlocked.Exchange(ref _lastUpdatedAt, _clock());
    }

    private static string Summarize(Exception e)
    {
        string? message = e.Message;
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message.Length > 160 ? message[..160] : message;
        }

        return e.GetType().Name;
    }

    public void Dispose()
    {
        _queue?.CompleteAdding();
        _worker?.Join(2000);
        _queue?.Dispose();
    }
}
