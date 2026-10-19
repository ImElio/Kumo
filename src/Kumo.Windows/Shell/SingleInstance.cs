using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Kumo.Windows.Shell;

/// <summary>
/// Ensures one Kumo per user session. Later launches hand their arguments to the running instance over a
/// named pipe restricted to the current user, then exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 256 * 1024;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    public SingleInstance(string applicationId)
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string scope = $"{applicationId}.{user}.{Process.GetCurrentProcess().SessionId}";
        _pipeName = scope;
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + scope, out bool createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    /// <summary>Starts accepting hand-overs. The callback runs on a thread-pool thread.</summary>
    public void Listen(Action<string[]> onArguments, Action<Exception>? onError = null)
    {
        if (!IsPrimary || _listener != null)
            return;
        _listener = Task.Run(() => AcceptLoop(onArguments, onError));
    }

    public bool TrySend(string[] arguments, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            byte[] payload = Encode(arguments);
            client.Write(payload);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The listener only ends through cancellation; its exception carries no information.
        }
        // ReleaseMutex is thread-affine; closing the last handle destroys the named mutex regardless of
        // which thread disposes, and only its existence is ever checked.
        _mutex.Dispose();
        _stop.Dispose();
    }

    // Paths cannot contain control characters on NTFS, so a newline-separated list is unambiguous.
    internal static byte[] Encode(string[] arguments) => Encoding.UTF8.GetBytes(string.Join('\n', arguments));

    internal static string[] Decode(ReadOnlySpan<byte> payload) =>
        payload.IsEmpty ? [] : Encoding.UTF8.GetString(payload).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private async Task AcceptLoop(Action<string[]> onArguments, Action<Exception>? onError)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);

                using var read = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                read.CancelAfter(TimeSpan.FromSeconds(3));
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[8192];
                int count;
                while ((count = await server.ReadAsync(chunk, read.Token).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, count);
                    if (buffer.Length > MaxMessageBytes)
                        throw new InvalidDataException("Hand-over message exceeds the size limit.");
                }
                onArguments(Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
            {
                onError?.Invoke(ex);
            }
        }
    }
}
