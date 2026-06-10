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

Console.WriteLine($"waffle_meter capture helper (elevated) — pipe '{pipeName}'. Ctrl+C to exit.");

// Ctrl+C terminates immediately even while blocked in WaitForConnection.
Console.CancelKeyPress += (_, _) => Environment.Exit(0);

using var server = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    maxNumberOfServerInstances: 1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);

server.WaitForConnection();
Console.WriteLine("client connected.");
try
{
    CaptureHostServer.Serve(
        server,
        (backendName, _) => backendName == "npcap" ? new NpcapBackend() : new WinDivertBackend(),
        Console.WriteLine);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"client session ended with error: {ex.Message}");
}

Console.WriteLine("client disconnected — exiting.");
return 0;
