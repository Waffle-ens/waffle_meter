using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using WaffleMeter.Capture.Live;

// Elevated capture helper. Serves ONE unelevated client over a named pipe (the app spawns a fresh
// helper each launch), then exits — so closing the app also tears down the helper + driver and leaves
// no orphan console. Reads the client's Start frame (backend + CaptureConfig), runs that capture
// backend, and forwards every CapturedSegment until the client disconnects/sends Stop; a start
// failure is reported as an Error frame.
//
//   WaffleMeter.CaptureHost [pipeName]
//
// The pipe is created with an EXPLICIT security descriptor (BuildPipeSecurity): the default security
// gives the pipe this elevated helper's HIGH integrity, which blocks the unelevated (medium-integrity)
// UI of the same user from connecting ("could not connect to the capture helper pipe" — running the app
// as admin sidesteps it). We grant the current user + authenticated users and label the pipe LOW so the
// medium UI can connect, with a graceful fallback to the default pipe so it never regresses.

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

// DACL letting the unelevated, same-user UI connect: current user full + Authenticated Users read/write
// + SYSTEM/Administrators (default parity). Strictly broader than the default — a client that already
// connects keeps working. Returns null (→ default pipe) if it ever fails, so we never regress.
// NOTE: the LOW mandatory integrity LABEL is applied separately (TrySetLowIntegrityLabel) because the
// managed PipeSecurity silently drops the label ACE when serialized.
PipeSecurity? BuildPipeSecurity()
{
    try
    {
        string user = WindowsIdentity.GetCurrent().User!.Value;
        string sddl = $"D:(A;;FA;;;{user})(A;;GRGW;;;AU)(A;;FA;;;SY)(A;;FA;;;BA)";
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        return security;
    }
    catch (Exception ex)
    {
        Log($"could not build pipe security ({ex.GetType().Name}: {ex.Message}); using default ACL.");
        return null;
    }
}

// Lower the pipe's mandatory integrity label to LOW (no-write-up) via Win32, so the unelevated
// (medium-integrity) UI of the same user can connect to this elevated helper's pipe. Managed PipeSecurity
// can't carry the label, so set it on the live handle. Best-effort: logs and continues on failure (the
// DACL grant still applies, so this never makes things worse).
void TrySetLowIntegrityLabel(NamedPipeServerStream pipe)
{
    IntPtr pSd = IntPtr.Zero;
    try
    {
        if (!NativeLabel.ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;LW)", 1, out pSd, out _))
        {
            Log($"low-IL label: SDDL convert failed (err {Marshal.GetLastWin32Error()})");
            return;
        }

        if (!NativeLabel.GetSecurityDescriptorSacl(pSd, out bool present, out IntPtr pSacl, out _) || !present)
        {
            Log("low-IL label: no SACL parsed");
            return;
        }

        uint rc = NativeLabel.SetSecurityInfo(
            pipe.SafePipeHandle.DangerousGetHandle(),
            NativeLabel.SE_KERNEL_OBJECT, NativeLabel.LABEL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, pSacl);
        Log(rc == 0 ? "pipe integrity label set to Low." : $"low-IL label: SetSecurityInfo failed (err {rc})");
    }
    catch (Exception ex)
    {
        Log($"low-IL label: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        if (pSd != IntPtr.Zero)
        {
            NativeLabel.LocalFree(pSd);
        }
    }
}

Log($"waffle_meter capture helper (elevated) — pipe '{pipeName}'. Ctrl+C to exit.");

// Ctrl+C terminates immediately even while blocked in WaitForConnection.
Console.CancelKeyPress += (_, _) => Environment.Exit(0);

NamedPipeServerStream server;
try
{
    PipeSecurity? security = BuildPipeSecurity();
    server = security is null
        ? new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
        : NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 0, outBufferSize: 0, pipeSecurity: security);
}
catch (IOException)
{
    // The pipe name is already owned by another helper instance — e.g. a launch race where the client's
    // no-prompt scheduled task AND its runas fallback both started a helper. Serve-once means one is
    // enough, so the loser just exits quietly instead of crashing with an unhandled "pipe busy".
    Log("pipe already served by another helper instance — exiting.");
    return 0;
}

TrySetLowIntegrityLabel(server);

using (server)
{
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
}

Log("client disconnected — exiting.");
return 0;

// Win32 for the mandatory-label SACL (managed PipeSecurity can't set it). Lowering an object's label
// to <= the caller's own integrity (High helper → Low pipe) needs no special privilege.
static class NativeLabel
{
    public const int SE_KERNEL_OBJECT = 6;
    public const uint LABEL_SECURITY_INFORMATION = 0x00000010;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor, uint sddlRevision, out IntPtr securityDescriptor, out uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSecurityDescriptorSacl(
        IntPtr securityDescriptor, [MarshalAs(UnmanagedType.Bool)] out bool saclPresent,
        out IntPtr sacl, [MarshalAs(UnmanagedType.Bool)] out bool saclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        IntPtr handle, int objectType, uint securityInfo,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr mem);
}
