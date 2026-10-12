using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace BroLedger.App.Services;

/// <summary>
/// LRU image cache (max 300 entries). Ported from Java service/ImageCache.java.
/// Returns a gray placeholder for any path that cannot be loaded.
/// </summary>
// ponytail: single global lock (Java used a synchronized map — same coarse ceiling);
// per-key stripes only if profiling ever shows contention.
public sealed class ImageCache
{
    private const int MaxSize = 300;

    private static ImageCache? _instance;
    public static ImageCache Instance => _instance ??= new();

    private readonly object _gate = new();
    private readonly Dictionary<string, Bitmap> _cache = new(MaxSize + 1);
    private readonly LinkedList<string> _lru = new();
    // Lazy: RenderTargetBitmap needs a live Avalonia platform — constructing it inside the
    // singleton's ctor crashes non-headless (pure VM) unit tests that touch UiContext.
    private Bitmap? _placeholder;

    private ImageCache()
    {
    }

    /// <summary>
    /// Load an image from <paramref name="gameArtRoot"/>/<paramref name="relativePath"/>.
    /// Returns the placeholder if the file does not exist or fails to load.
    /// </summary>
    public Bitmap Get(string gameArtRoot, string? relativePath)
    {
        if (relativePath is null) return GetPlaceholder();

        var key = gameArtRoot + "/" + relativePath;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _lru.Remove(key);
                _lru.AddFirst(key);
                return cached;
            }

            var bitmap = LoadOrPlaceholder(gameArtRoot, relativePath);
            _cache[key] = bitmap;
            _lru.AddFirst(key);
            if (_cache.Count > MaxSize)
            {
                var eldest = _lru.Last!.Value;
                _lru.RemoveLast();
                _cache.Remove(eldest);
                // ponytail: evicted bitmaps are left to the finalizer, like Java relies
                // on GC; call Dispose here only if memory pressure ever shows up.
            }

            return bitmap;
        }
    }

    public Bitmap GetPlaceholder() => _placeholder ??= BuildPlaceholder();

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }

    private Bitmap LoadOrPlaceholder(string gameArtRoot, string relativePath)
    {
        var file = Path.Combine(gameArtRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return GetPlaceholder();
        try
        {
            return new Bitmap(file);
        }
        catch (Exception)
        {
            return GetPlaceholder();
        }
    }

    // Java ImageCache.buildPlaceholder (L69-79) inlines a hand-assembled 1×1 gray PNG whose
    // IDAT chunk is corrupt (its CRC should be 0x8405A4EF, and the zlib stream itself is
    // invalid — the Java author's hand-typed bytes; the IHDR CRC 0x3A7E9B55 is the correct
    // canonical value). JavaFX's lenient loader swallowed it, Skia throws "Unable to load
    // bitmap" (crashed first launch).
    // Java 的占位图实际从未渲染成功——所有占位场景（空装备槽、缺口图）在 Java 里都是空白。
    // 1:1 视觉对齐：占位图改为全透明 1×1（灰块会在总览装备列/卡片/缺口图处显示 Java 没有
    // 的灰色方块——用户已两次报告）。
    private static Bitmap BuildPlaceholder()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(1, 1), new Vector(96, 96));
        return rtb; // 未绘制的 RenderTargetBitmap 即全透明
    }
}
