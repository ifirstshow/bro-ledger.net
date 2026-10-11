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

    // 1×1 gray PNG, bytes ported verbatim from Java ImageCache.buildPlaceholder (L69-79)
    // so no resource file is needed.
    private static Bitmap BuildPlaceholder()
    {
        byte[] grayPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,          // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,          // IHDR chunk (13 bytes)
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,          // width=1, height=1
            0x08, 0x00, 0x00, 0x00, 0x00, 0x3A, 0x7E, 0x9B, 0x55,    // bit depth=8, color=grayscale, crc
            0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,          // IDAT chunk (10 bytes)
            0x78, 0x9C, 0x62, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, // zlib-compressed gray pixel
            0xE2, 0x21, 0xBC, 0x33,                                  // IDAT crc
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,          // IEND chunk
            0xAE, 0x42, 0x60, 0x82,                                  // IEND crc
        ];
        return new Bitmap(new MemoryStream(grayPng));
    }
}
