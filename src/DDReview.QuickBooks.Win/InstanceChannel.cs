using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DDReview.QuickBooks.Win;

/// <summary>
/// One poster per Windows session. A second start (for example a second click on the website's button) does not open a second window:
/// it hands its link to the window that is already open over a named pipe that only the same Windows user can use, then exits.
/// The text that arrives is untrusted; the window checks it like any other link.
/// </summary>
static class InstanceChannel
{
    public const string MutexName = @"Local\DDReview.QuickBooksPoster";
    public const int MaxBytes = 4096;

    public static string PipeName =>
        "DDReview.QuickBooksPoster." + (WindowsIdentity.GetCurrent().User?.Value ?? "user") + "." + Process.GetCurrentProcess().SessionId;

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    const int AnyProcess = -1;

    /// <summary>Sends a link (or, with null, just "come to the front") to the window already open. False if nobody answered.</summary>
    public static bool Send(string? link) => SendMessage(link is null ? "SHOW" : "LINK " + link);

    /// <summary>Hands a package file (by its path) to the window already open.</summary>
    public static bool SendPackage(string path) => SendMessage("PACKAGE " + path);

    static bool SendMessage(string message)
    {
        try
        {
            AllowSetForegroundWindow(AnyProcess);   // this process was just started by a click, so it may let the open window come to the front
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            c.Connect(5000);
            var bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length > MaxBytes) return false;
            c.Write(bytes, 0, bytes.Length);
            c.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Listens on the pipe for the next copy's link. Callbacks run on a worker thread; the caller moves them to the window's thread.</summary>
sealed class InstanceServer : IDisposable
{
    readonly CancellationTokenSource _cts = new();

    public void Start(Action<string> onLink, Action onShow, Action<string> onPackage) => _ = Task.Run(() => Loop(onLink, onShow, onPackage));

    async Task Loop(Action<string> onLink, Action onShow, Action<string> onPackage)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(InstanceChannel.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var read = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                read.CancelAfter(TimeSpan.FromSeconds(3));
                var buffer = new byte[InstanceChannel.MaxBytes + 1];
                int n = 0;
                while (n < buffer.Length)
                {
                    int got = await pipe.ReadAsync(buffer.AsMemory(n), read.Token);
                    if (got == 0) break;
                    n += got;
                }
                if (n > InstanceChannel.MaxBytes) continue;
                var message = Encoding.UTF8.GetString(buffer, 0, n).Trim();
                if (message == "SHOW") onShow();
                else if (message.StartsWith("LINK ", StringComparison.Ordinal)) onLink(message[5..]);
                else if (message.StartsWith("PACKAGE ", StringComparison.Ordinal)) onPackage(message[8..]);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { /* a sender that stalled: take the next one */ }
            catch (IOException) { /* the sender went away */ }
            catch (Exception) { await Task.Delay(500); }
        }
    }

    public void Dispose() => _cts.Cancel();
}
