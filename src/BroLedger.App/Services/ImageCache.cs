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
    private readonly Bitmap _placeholder = BuildPlaceholder();

    private ImageCache()
    {
    }

    /// <summary>
    /// Load an image from <paramref name="gameArtRoot"/>/<paramref name="relativePath"/>.
    /// Returns the placeholder if the file does not exist or fails to load.
    /// </summary>
    public Bitmap Get(string gameArtRoot, string? relativePath)
    {
        if (relativePath is null) return _placeholder;

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

    public Bitmap GetPlaceholder() => _placeholder;

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
        if (!File.Exists(file)) return _placeholder;
        try
        {
            return new Bitmap(file);
        }
        catch (Exception)
        {
            return _placeholder;
        }
    }

    // Java ImageCache.buildPlaceholder (L69-79) inlines a hand-assembled 1×1 gray PNG whose
    // IHDR CRC is wrong (0x3A7E9B55; correct is 0xC4A0EB47) — JavaFX's lenient loader
    // swallowed it, Skia throws "Unable to load bitmap" (crashed first launch). Draw the
    // same 1×1 gray square natively instead of porting the corrupt bytes.
    private static Bitmap BuildPlaceholder()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(1, 1), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
            ctx.FillRectangle(Brushes.Gray, new Rect(0, 0, 1, 1));
        return rtb;
    }
}
