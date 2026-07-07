using System.IO;
using System.Reflection;

namespace WaffleMeter.Replay;

/// <summary>
/// Discovers the private replay engine at runtime. The engine ships as <c>WaffleMeter.Replay.dll</c>
/// beside the executable (injected into the release by CI; copied by a dev build target locally). When
/// present, its <see cref="IReplayEngineFactory"/> implementation is loaded by reflection; when absent —
/// e.g. an open-source build without the private module — this returns null and the app runs with replay
/// simply unavailable. Never throws; the result is cached (the probe runs once).
/// </summary>
public static class ReplayEngineLoader
{
    /// <summary>The assembly file the private engine ships as.</summary>
    private const string EngineAssembly = "WaffleMeter.Replay.dll";

    private static IReplayEngineFactory? _factory;
    private static bool _probed;

    /// <summary>Locate and instantiate the engine factory, or null if the engine DLL isn't present /
    /// can't be loaded. The default (app base directory) probe is cached after the first call; passing an
    /// explicit <paramref name="probeDir"/> always does a fresh probe (so it's testable).</summary>
    /// <param name="probeDir">Directory to look in (default: the app base directory, cached).</param>
    public static IReplayEngineFactory? TryLoad(string? probeDir = null)
    {
        if (probeDir is null)
        {
            if (_probed)
            {
                return _factory;
            }

            _probed = true;
            return _factory = Probe(AppContext.BaseDirectory);
        }

        return Probe(probeDir);
    }

    private static IReplayEngineFactory? Probe(string dir)
    {
        try
        {
            string path = Path.Combine(dir, EngineAssembly);
            if (!File.Exists(path))
            {
                return null;
            }

            Assembly asm = Assembly.LoadFrom(path);
            Type? impl = Array.Find(
                asm.GetTypes(),
                t => !t.IsAbstract && !t.IsInterface && typeof(IReplayEngineFactory).IsAssignableFrom(t));

            return impl is null ? null : (IReplayEngineFactory?)Activator.CreateInstance(impl);
        }
        catch
        {
            // a missing/incompatible engine must never break app startup — replay just stays unavailable
            return null;
        }
    }

    /// <summary>True if the engine DLL is present and loadable.</summary>
    public static bool IsAvailable(string? probeDir = null) => TryLoad(probeDir) is not null;
}
