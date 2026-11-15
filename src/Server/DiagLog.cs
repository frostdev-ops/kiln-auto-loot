// SPDX-License-Identifier: MIT
// Kiln Auto Loot's own log file (Kiln has no per-mod log file): lines "[HH:mm:ss] text"
// appended to <DataDirectory>/kiln-auto-loot.log by a background thread, so the tick thread never touches the disk.
// Size cap: at 4 MiB the file becomes kiln-auto-loot.log.old (one old file kept).
using System.Collections.Concurrent;

namespace KilnAutoLoot.Server;

internal sealed class DiagLog : IDisposable
{
    public const long MaxBytes = 4L * 1024 * 1024;
    private const int MaxQueued = 4096;

    private readonly string _path;
    private readonly BlockingCollection<string> _queue = new(MaxQueued);
    private readonly Thread _thread;

    public DiagLog(string path)
    {
        _path = path;
        _thread = new Thread(Run) { IsBackground = true, Name = "kiln-auto-loot log" };
        _thread.Start();
    }

    /// <summary>Any thread; drops the line when the writer is far behind.</summary>
    public void Write(string text)
    {
        if (_queue.IsAddingCompleted) return;
        try { _queue.TryAdd($"[{DateTime.Now:HH:mm:ss}] {text}"); }
        catch (InvalidOperationException) { }
    }

    private void Run()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    var info = new FileInfo(_path);
                    if (info.Exists && info.Length > MaxBytes) File.Move(_path, _path + ".old", overwrite: true);
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
