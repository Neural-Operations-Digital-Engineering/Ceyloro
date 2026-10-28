using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Ceyloro.Windows;

internal sealed class SingleInstanceGate : IDisposable
{
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private static readonly uint ActivateMessage = RegisterWindowMessage($"Ceyloro.Activate.{Environment.UserName}");
    private static readonly string PipeName = $"CeyloroPipe-{Environment.UserName}";

    private readonly Mutex mutex;
    private readonly CancellationTokenSource cts = new();
    private Task? pipeServerTask;

    public static event Action<string[]>? FilesReceived;

    private SingleInstanceGate(Mutex mutex)
    {
        this.mutex = mutex;
        StartPipeServer();
    }

    public static int ActivationMessage => unchecked((int)ActivateMessage);

    public static SingleInstanceGate? TryAcquire(string[]? args = null)
    {
        var files = args != null ? ParseFileArguments(args) : [];
        var mutex = new Mutex(initiallyOwned: true, $"Local\\Ceyloro-{Environment.UserName}", out var createdNew);
        if (createdNew)
            return new SingleInstanceGate(mutex);

        mutex.Dispose();

        // Instance kedua: kirim argumen berkas ke instance utama via Named Pipe
        if (files.Length > 0)
        {
            SendFilesToRunningInstance(files);
        }
        else if (ActivateMessage != 0)
        {
            PostMessage(HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }

        return null;
    }

    public static string[] ParseFileArguments(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (arg.Equals("--files", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--context-menu", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (File.Exists(arg) || Directory.Exists(arg))
                result.Add(Path.GetFullPath(arg));
        }
        return result.ToArray();
    }

    private static void SendFilesToRunningInstance(string[] files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            foreach (var file in files)
                writer.WriteLine(file);
            writer.Flush();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Gagal mengirim pesan IPC: {ex.Message}");
            if (ActivateMessage != 0)
                PostMessage(HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void StartPipeServer()
    {
        pipeServerTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var files = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var trimmed = line.Trim();
                        if (File.Exists(trimmed) || Directory.Exists(trimmed))
                            files.Add(trimmed);
                    }

                    if (files.Count > 0)
                    {
                        FilesReceived?.Invoke(files.ToArray());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLog.Write($"NamedPipeServerStream error: {ex.Message}");
                    try { await Task.Delay(500, cts.Token); } catch { }
                }
            }
        }, cts.Token);
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
        try { mutex.ReleaseMutex(); }
        catch { }
        mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
