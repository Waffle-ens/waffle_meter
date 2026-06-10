using System.IO.Pipes;
using WaffleMeter.Capture.Live;

// Elevated capture helper. Serves ONE unelevated client over a named pipe (the app spawns a fresh
// helper each launch), then exits — so closing the app also tears down the helper + driver and leaves
// no orphan console. Reads the client's Start frame (backend + CaptureConfig), runs that capture
// backend, and forwards every CapturedSegment until the client disconnects/sends Stop; a start
// failure is reported as an Error frame.
//
//   WaffleMeter.CaptureHost [pipeName]
//
// Same-user elevated->medium handshake works on the default pipe DACL (the user's own SID is granted).

string pipeName = args.Length >= 1 ? args[0] : CaptureWireProtocol.DefaultPipeName;

// Log to a file next to the exe too, so the exit reason survives the (serve-once) console closing.
string logPath = Path.Combine(AppContext.BaseDirectory, "helper.log");
void Log(string message)
{
    Console.WriteLine(message);
    try
    {
        File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} {message}\n");
    }
    catch
    {
        // best effort
    }
}

Log($"waffle_meter capture helper (elevated) — pipe '{pipeName}'. Ctrl+C to exit.");

// Ctrl+C terminates immediately even while blocked in WaitForConnection.
Console.CancelKeyPress += (_, _) => Environment.Exit(0);

using var server = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    maxNumberOfServerInstances: 1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);

server.WaitForConnection();
Log("client connected.");
try
{
    CaptureHostServer.Serve(
        server,
        (backendName, _) => backendName == "npcap" ? new NpcapBackend() : new WinDivertBackend(),
        Log);
}
catch (Exception ex)
{
    Log($"session error: {ex.GetType().Name}: {ex.Message}");
}

Log("client disconnected — exiting.");
return 0;
