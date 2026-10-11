using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

/// <summary>
/// Watches a save file for external modifications and fires a callback on the UI thread.
///
/// <para>Stability loop: after detecting a modify event, polls the file size every 250 ms up to
/// 6 times; fires only when two consecutive readings are equal (write has settled).</para>
/// </summary>
// Transcribed method-by-method from Java service/FileWatcherService.java.
// Java WatchService.take() (blocking) → FileSystemWatcher.Changed → BlockingCollection,
// consumed via GetConsumingEnumerable(): blocks per event and exits when Stop completes
// the collection (the ClosedWatchServiceException exit path). OVERFLOW (Java: continue)
// → Error event (buffer overflow): logged at trace, never triggers onChange, watching
// continues. Java Thread.interrupt during the settle sleep → ManualResetEventSlim signal.
public sealed class FileWatcherService
{
    private const int  PollIntervalMs  = 250;
    private const int  PollMaxAttempts = 6;
    private const long DebounceMs      = 2_000;

    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    // Java volatile long → Volatile.Read/Write（写：watch 线程；复位：Stop 线程）。
    private long _lastFiredAt;

    private FileSystemWatcher? _watcher;
    private BlockingCollection<bool>? _events;
    private ManualResetEventSlim? _stopSignal;

    // ---- lifecycle ---------------------------------------------------------

    /// <summary>
    /// Start watching <paramref name="savePath"/>. Fires <paramref name="onChange"/> through
    /// <paramref name="marshal"/> (UI-thread seam — Java Platform.runLater; App 层传
    /// Dispatcher.UIThread.Invoke，测试传直接调用) whenever the file appears to be fully
    /// written. Any previous watcher is stopped first.
    /// </summary>
    public void Watch(string savePath, Action onChange, Action<Action> marshal)
    {
        Stop();

        var fullPath = Path.GetFullPath(savePath);
        var dir = Path.GetDirectoryName(fullPath);
        var target = Path.GetFileName(fullPath);

        FileSystemWatcher watcher;
        var events = new BlockingCollection<bool>();
        var stopSignal = new ManualResetEventSlim(false);
        try
        {
            // Java: dir.register(watchService, ENTRY_MODIFY) — 目录级监视，只关心 MODIFY。
            watcher = new FileSystemWatcher(dir!, target) { NotifyFilter = NotifyFilters.LastWrite };
            watcher.Changed += (_, _) => events.TryAdd(true);
            watcher.Error += (_, e) =>
                Log.LogTrace("FileWatcherService: watch buffer overflow — {Message}", e.GetException().Message);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            Log.LogWarning("FileWatcherService: cannot register watch — {Message}", e.Message);
            stopSignal.Dispose();
            return;
        }

        _watcher = watcher;
        _events = events;
        _stopSignal = stopSignal;

        var thread = new Thread(() => WatchLoop(fullPath, events, stopSignal, onChange, marshal))
        {
            IsBackground = true, // ≈ setDaemon(true)
            Name = "file-watcher",
        };
        thread.Start();
    }

    /// <summary>Stop the watcher. Safe to call even if not currently watching.</summary>
    public void Stop()
    {
        Volatile.Write(ref _lastFiredAt, 0);
        if (_watcher != null)
        {
            try { _watcher.Dispose(); }
            catch (Exception e) { Log.LogDebug("FileWatcherService: close — {Message}", e.Message); }
            _watcher = null;
        }
        // ≈ watchService.close() + watchThread.interrupt():CompleteAdding 让阻塞的
        // GetConsumingEnumerable 结束（≈ ClosedWatchServiceException），信号唤醒
        // settle 轮询中的等待（≈ Thread.interrupt）。
        _stopSignal?.Set();
        _events?.CompleteAdding();
        _stopSignal = null;
        _events = null;
    }

    // ---- internal ----------------------------------------------------------

    private void WatchLoop(string savePath, BlockingCollection<bool> events,
        ManualResetEventSlim stopSignal, Action onChange, Action<Action> marshal)
    {
        // GetConsumingEnumerable ≡ take() 循环：逐事件阻塞，CompleteAdding 后退出。
        foreach (var _ in events.GetConsumingEnumerable())
        {
            // Filter=target ⇒ 出队的每个事件都相关（Java 比较事件 context 文件名）。
            if (!HasSettled(savePath, stopSignal)) continue;

            var now = Environment.TickCount64;
            if (now - Volatile.Read(ref _lastFiredAt) >= DebounceMs)
            {
                Volatile.Write(ref _lastFiredAt, now);
                marshal(onChange); // ≈ Platform.runLater(onChange)
            }
            else
            {
                Log.LogDebug("FileWatcherService: suppressed duplicate event within debounce window");
            }
        }
    }

    private bool HasSettled(string savePath, ManualResetEventSlim stopSignal)
    {
        long prev = -1;
        var stable = 0;
        for (var i = 0; i < PollMaxAttempts; i++)
        {
            // ≈ Thread.sleep(POLL_INTERVAL_MS) + interrupt → return false。
            if (stopSignal.Wait(PollIntervalMs)) return false;
            long size;
            try
            {
                size = new FileInfo(savePath).Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.LogDebug("FileWatcherService: size poll — {Message}", e.Message);
                continue;
            }
            if (size > 0 && size == prev)
            {
                if (++stable >= 2) return true;
            }
            else
            {
                stable = 0;
            }
            prev = size;
        }
        return stable >= 1;
    }
}
