using System.IO.Pipes;
using WaffleMeter.Capture.Live;

// Elevated capture helper. Serves one unelevated client at a time over a named pipe: it waits for a
// connection, reads the client's Start frame (which backend + CaptureConfig), runs that capture
// backend, and forwards every CapturedSegment as a Segment frame until the client disconnects or
// sends Stop. A capture-start failure is reported as an Error frame, then the connection is dropped.
//
//   WaffleMeter.CaptureHost [pipeName]
//
// The same-user elevated->medium handshake works on the default pipe DACL (the user's own SID is
// granted), so no custom ACL is needed for the single-user deployment.

string pipeName = args.Length >= 1 ? args[0] : CaptureWireProtocol.DefaultPipeName;

Console.WriteLine($"waffle_meter capture helper (elevated) — pipe '{pipeName}'. Ctrl+C to exit.");

bool exiting = false;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exiting = true;
};

while (!exiting)
{
    using var server = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    try
    {
        server.WaitForConnection();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WaitForConnection failed: {ex.Message}");
        continue;
    }

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

    Console.WriteLine("client disconnected.");
}

return 0;
