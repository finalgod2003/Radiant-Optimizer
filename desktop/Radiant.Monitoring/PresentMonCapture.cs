using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Radiant.Monitoring;

public sealed class PresentMonCapture : IAsyncDisposable
{
    public const string Version = "2.6.0";
    public const string ExpectedSha256 = "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
    public const long MaximumOutputBytes = 32 * 1024 * 1024;
    private readonly string executablePath;
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private TaskCompletionSource? stopped;
    private bool disposed;
    public PresentMonCapture(string executablePath) => this.executablePath = Path.GetFullPath(executablePath);
    public static PresentMonCapture CreateBundled() => new(Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon", "PresentMon.exe"));

    public async Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try { using var handle = await OpenVerifiedExecutableAsync(cancellationToken).ConfigureAwait(false); return null; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException) { return error.Message; }
    }

    public async Task<CaptureResult> CaptureAsync(ProcessIdentity target, TimeSpan duration,
        Action<FrameUpdate>? onUpdate = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Pid <= 0) throw new ArgumentOutOfRangeException(nameof(target), "Select a valid process ID.");
        if (duration < TimeSpan.FromSeconds(5) || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(duration), "Capture must last between 5 and 300 seconds.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active is not null) throw new InvalidOperationException("A frame capture is already running.");
            active = cancellation; stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var start = DateTimeOffset.UtcNow;
        var samples = new FrameAccumulator(target.Pid);
        var parser = new PresentMonCsvParser();
        string? reason = null;
        int? exitCode = null;
        Process? helper = null;
        Task? outputTask = null, errorTask = null;
        var diagnostic = new StringBuilder();
        var sessionName = "Optimizer-" + Guid.NewGuid().ToString("N");
        try
        {
            // This held file handle denies replacement or rewriting of the verified executable
            // until our own helper exits. No PATH lookup, shell, or automatic elevation.
            using var executable = await OpenVerifiedExecutableAsync(cancellation.Token).ConfigureAwait(false);
            using var selected = Process.GetProcessById(target.Pid);
            _ = selected.Handle;
            if (selected.HasExited || selected.StartTime.ToUniversalTime() != target.StartedAt.UtcDateTime
                || !string.Equals(selected.ProcessName, target.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected process exited or its identity changed. Select it again.");
            var info = BuildStartInfo(executablePath, target.Pid, duration, sessionName);
            helper = Process.Start(info) ?? throw new InvalidOperationException("Could not start the bundled PresentMon.");
            // The helper has its own timer; this independent bound also handles hangs and start-up delays.
            cancellation.CancelAfter(duration + TimeSpan.FromSeconds(8));
            outputTask = ReadOutputAsync(helper.StandardOutput, async line =>
            {
                // The retained Windows process handle refers to this process instance, even after PID reuse.
                if (selected.HasExited) { reason = "The selected process exited."; cancellation.Cancel(); return; }
                if (parser.TryRead(line, DateTimeOffset.UtcNow, out var frame) && frame is not null)
                {
                    if (frame.TimeSeconds > duration.TotalSeconds + 5) throw new InvalidDataException("Frame timestamp exceeded the capture bound.");
                    samples.Add(frame);
                    if (samples.AtCapacity) { reason = "Capture reached the 60,000-frame memory limit."; cancellation.Cancel(); }
                }
                await Task.CompletedTask;
            }, cancellation.Token);
            errorTask = ReadDiagnosticsAsync(helper.StandardError, diagnostic, cancellation.Token);
            while (!helper.HasExited && !cancellation.IsCancellationRequested)
            {
                if (selected.HasExited) { reason = "The selected process exited."; cancellation.Cancel(); break; }
                if (outputTask.IsFaulted) await outputTask.ConfigureAwait(false);
                if (errorTask.IsFaulted) await errorTask.ConfigureAwait(false);
                var summary = samples.SummarizeLive(DateTimeOffset.UtcNow);
                onUpdate?.Invoke(new(summary, summary is null ? "Waiting for recent frames from the selected process." : null,
                    samples.Snapshot().Length, parser.RejectedRows + samples.RejectedRows));
                await Task.Delay(500, cancellation.Token).ConfigureAwait(false);
            }
            if (!cancellation.IsCancellationRequested)
            {
                await helper.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                exitCode = helper.ExitCode;
                if (exitCode != 0) reason = DiagnosticReason(exitCode.Value, diagnostic.ToString());
            }
            else reason ??= cancellationToken.IsCancellationRequested ? "Capture stopped." : "Capture reached its time limit or was stopped.";
        }
        catch (OperationCanceledException)
        {
            reason ??= cancellationToken.IsCancellationRequested ? "Capture stopped." : "Capture reached its time limit or was stopped.";
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { reason = error.Message; }
        finally
        {
            try
            {
                cancellation.Cancel();
                if (helper is not null)
                {
                    try
                    {
                        if (!helper.HasExited) helper.Kill(entireProcessTree: true);
                        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await helper.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                        exitCode ??= helper.ExitCode;
                    }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception or OperationCanceledException or AggregateException)
                    { reason = (reason is null ? "" : reason + " ") + "The capture helper could not be confirmed stopped: " + error.Message; }
                    try
                    {
                        // Only the cryptographically unique session created by this capture is stopped.
                        var cleanup = StopOwnTrace(sessionName);
                        if (cleanup is not 0 and not 4201)
                            reason = (reason is null ? "" : reason + " ") + $"ETW cleanup returned {cleanup}.";
                    }
                    catch (Exception error) when (error is Win32Exception or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
                    { reason = (reason is null ? "" : reason + " ") + "ETW cleanup could not be confirmed: " + error.Message; }
                    finally { helper.Dispose(); }
                }
                if (outputTask is not null) { try { await outputTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or InvalidOperationException or TimeoutException) { } }
                if (errorTask is not null) { try { await errorTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or InvalidOperationException or TimeoutException) { } }
            }
            finally { lock (gate) { active = null; stopped?.TrySetResult(); } }
        }
        var frames = samples.Snapshot();
        var total = FrameAccumulator.Summarize(frames);
        if (total is null)
            reason ??= "No usable frames were recorded. The process may not be presenting, or Windows ETW access is unavailable. Run with an account allowed to read performance traces; this app never elevates automatically.";
        onUpdate?.Invoke(new(null, "Capture finished; the summary below describes the recorded interval.", frames.Length, parser.RejectedRows + samples.RejectedRows));
        return new(target, start, DateTimeOffset.UtcNow, frames, total, reason, exitCode);
    }

    internal static ProcessStartInfo BuildStartInfo(string executable, int pid, TimeSpan duration, string sessionName)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in new[] { "--process_id", pid.ToString(CultureInfo.InvariantCulture), "--output_stdout", "--v1_metrics",
            "--session_name", sessionName, "--timed", Math.Ceiling(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            "--terminate_after_timed", "--terminate_on_proc_exit", "--no_console_stats", "--no_track_input" }) info.ArgumentList.Add(arg);
        return info;
    }

    private async Task<FileStream> OpenVerifiedExecutableAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new InvalidOperationException("The bundled PresentMon requires 64-bit Windows.");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The verified PresentMon tool is missing. Reinstall the complete client preview.");
        for (FileSystemInfo? part = new FileInfo(executablePath); part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("PresentMon must not be loaded through a symbolic link or reparse point.");
        var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != 980_320) throw new InvalidDataException("The bundled PresentMon has an unexpected size. Reinstall the complete client preview.");
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, ExpectedSha256, StringComparison.Ordinal)) throw new InvalidDataException("The bundled PresentMon integrity check failed. Reinstall the complete client preview.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static async Task ReadOutputAsync(TextReader reader, Func<string, Task> onLine, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        long bytes = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            bytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (bytes > MaximumOutputBytes) throw new InvalidDataException("PresentMon reached the 32 MiB output limit.");
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n') { await onLine(line.ToString().TrimEnd('\r')).ConfigureAwait(false); line.Clear(); }
                else { line.Append(buffer[i]); if (line.Length > PresentMonCsvParser.MaximumLineLength) throw new InvalidDataException("PresentMon returned an oversized CSV record."); }
            }
        }
        if (line.Length > 0) await onLine(line.ToString().TrimEnd('\r')).ConfigureAwait(false);
    }

    private static async Task ReadDiagnosticsAsync(TextReader reader, StringBuilder diagnostic, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return;
            var take = Math.Min(read, 8192 - diagnostic.Length);
            if (take > 0) diagnostic.Append(buffer, 0, take);
        }
    }
    private static string DiagnosticReason(int exitCode, string text)
    {
        var clean = new string(text.Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t').Take(2000).ToArray()).Trim();
        return $"PresentMon exited with code {exitCode}. " + clean
            + " Windows ETW capture requires administrator access or membership in Performance Log Users; no permissions were changed.";
    }

    public async ValueTask DisposeAsync()
    {
        Task? wait;
        lock (gate) { disposed = true; active?.Cancel(); wait = stopped?.Task; }
        if (wait is not null) await wait.ConfigureAwait(false);
    }

    private static uint StopOwnTrace(string sessionName)
    {
        var size = Marshal.SizeOf<TraceProperties>();
        var data = new TraceProperties { Wnode = new() { BufferSize = checked((uint)(size + (sessionName.Length + 1) * 2)) }, LoggerNameOffset = (uint)size };
        var buffer = Marshal.AllocHGlobal((int)data.Wnode.BufferSize);
        try
        {
            Marshal.Copy(new byte[data.Wnode.BufferSize], 0, buffer, (int)data.Wnode.BufferSize);
            Marshal.StructureToPtr(data, buffer, false);
            Marshal.Copy((sessionName + '\0').ToCharArray(), 0, IntPtr.Add(buffer, size), sessionName.Length + 1);
            return ControlTrace(0, sessionName, buffer, 1);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WnodeHeader
    { public uint BufferSize, ProviderId; public ulong HistoricalContext, TimeStamp; public Guid Guid; public uint ClientContext, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct TraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags, AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }
    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)] private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint code);
}
