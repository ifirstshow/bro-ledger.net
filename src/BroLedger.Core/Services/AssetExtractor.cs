using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

// 真源 JAVA/service/AssetExtractor.java，逐方法移植。无独立测试（Java 亦无，需真实 .dat），计划⑤手验。
public sealed class AssetExtractor
{
    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    private const string VersionFile = "game-art-version.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public sealed record ExtractionResult(int Total, int Extracted, int Missing, int Fallback);

    public delegate void ProgressListener(int done, int total, string currentFile);

    // LinkedHashSet → string[] 保序（fallback 前缀过滤依赖顺序无去重需求，数组即保序）
    private static readonly string[] PatternPrefixes =
    [
        "gfx/ui/items/accessory/",
        "gfx/ui/items/ammo/",
        "gfx/ui/items/armor/",
        "gfx/ui/items/armor_upgrades/",
        "gfx/ui/backgrounds/",
        "gfx/ui/items/consumables/",
        "gfx/ui/items/helmets/",
        "gfx/ui/injury/",
        "gfx/ui/items/loot/",
        "gfx/ui/items/misc/",
        "gfx/ui/perks/",
        "gfx/ui/items/shields/",
        "gfx/skills/",
        "gfx/ui/items/supplies/",
        "gfx/ui/items/tools/",
        "gfx/ui/items/trade/",
        "gfx/ui/traits/",
        "gfx/ui/items/weapons/"
    ];

    // Java ZipFile(UTF_8)：显式指定 entry 名字符集；.NET ZipArchive 默认读 UTF-8 标志位，
    // 无标志位时回退 IBM437。BB 资产路径全 ASCII，两者等价；非 ASCII entry 名才会出现差异。
    public ExtractionResult ExtractMapped(string datFile, string outDir, ProgressListener? listener)
    {
        List<string> fileMap = LoadAssetMap();
        using ZipArchive zf = ZipFile.OpenRead(datFile);
        List<string> mapped = DoExtractMapped(zf, outDir, fileMap, listener);
        int missing = fileMap.Count - mapped.Count;
        WriteVersionFile(outDir);
        return new ExtractionResult(fileMap.Count, mapped.Count, missing, 0);
    }

    public ExtractionResult ExtractWithFallback(string datFile, string outDir, ProgressListener? listener)
    {
        List<string> fileMap = LoadAssetMap();
        using ZipArchive zf = ZipFile.OpenRead(datFile);
        List<ZipArchiveEntry> entries = [.. zf.Entries];
        int combinedTotal = fileMap.Count + entries.Count;

        int phase1Offset = fileMap.Count;
        ProgressListener? phase1Listener = listener == null ? null :
            (done, _, file) => listener(done, combinedTotal, file);
        ProgressListener? phase2Listener = listener == null ? null :
            (done, _, file) => listener(phase1Offset + done, combinedTotal, file);

        List<string> mapped = DoExtractMapped(zf, outDir, fileMap, phase1Listener);
        List<string> fallback = DoExtractFallback(zf, outDir, [.. mapped], entries, phase2Listener);
        int missing = fileMap.Count - mapped.Count;
        WriteVersionFile(outDir);
        return new ExtractionResult(fileMap.Count, mapped.Count, missing, fallback.Count);
    }

    private static List<string> DoExtractMapped(ZipArchive zf, string outDir,
        List<string> fileMap, ProgressListener? listener)
    {
        var mapped = new List<string>();
        int total = fileMap.Count;
        int done = 0;
        foreach (string src in fileMap)
        {
            string label;
            ZipArchiveEntry? zipEntry = zf.GetEntry(src);
            if (zipEntry == null)
            {
                Log.LogWarning("Archive missing: {Source}", src);
                label = "[missing] " + src;
            }
            else
            {
                ExtractTo(zipEntry, outDir, src);
                mapped.Add(src);
                label = src;
            }
            done++;
            listener?.Invoke(done, total, label);
        }
        return mapped;
    }

    private static List<string> DoExtractFallback(ZipArchive zf, string outDir,
        HashSet<string> alreadyMapped, List<ZipArchiveEntry> entries, ProgressListener? listener)
    {
        var fallback = new List<string>();
        int done = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            done++;
            string name = entry.FullName;
            if (alreadyMapped.Contains(name)) continue;
            if (!name.ToLowerInvariant().EndsWith(".png")) continue;
            bool relevant = false;
            foreach (string prefix in PatternPrefixes)
            {
                if (name.Contains(prefix, StringComparison.Ordinal)) { relevant = true; break; }
            }
            if (!relevant) continue;
            ExtractTo(entry, outDir, name);
            fallback.Add(name);
            listener?.Invoke(done, entries.Count, name);
        }
        return fallback;
    }

    // loadAssetMap：imageSlots 各子 map 的 value 并集，保序去重（Java Map src→src，值恒等故 List 足够）
    private static List<string> LoadAssetMap()
    {
        GameDataService gds = GameDataService.Instance;
        gds.LoadFromEmbeddedResource();

        var paths = new List<string>();
        var seen = new HashSet<string>();
        if (gds.GetImageSlots() is JsonObject imageSlots)
        {
            foreach (var (_, subMap) in imageSlots)
            {
                if (subMap is not JsonObject obj) continue;
                foreach (var (_, value) in obj)
                {
                    if (value is JsonValue v && v.TryGetValue<string>(out var s) && seen.Add(s))
                        paths.Add(s);
                }
            }
        }
        return paths;
    }

    /// <summary>
    /// Returns true if the game-art directory's version file is missing or older
    /// than the version declared in game-data.json.
    /// </summary>
    public static bool IsGameArtOutdated(string? gameArtDir)
    {
        if (gameArtDir == null || !Directory.Exists(gameArtDir)) return false;
        try
        {
            GameDataService gds = GameDataService.Instance;
            gds.LoadFromEmbeddedResource();
            int expected = gds.GetGameArtVersion();
            int actual = ReadExtractedVersion(gameArtDir);
            return actual < expected;
        }
        catch (Exception e)
        {
            Log.LogWarning("Could not check game-art version: {Message}", e.Message);
            return false;
        }
    }

    private static int ReadExtractedVersion(string gameArtDir)
    {
        string versionFile = Path.Combine(gameArtDir, VersionFile);
        if (!File.Exists(versionFile)) return -1;
        try
        {
            return JsonNode.Parse(File.ReadAllText(versionFile)) is JsonObject node
                && node["version"] is JsonValue v && v.TryGetValue<int>(out var i) ? i : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static void WriteVersionFile(string outDir)
    {
        try
        {
            GameDataService gds = GameDataService.Instance;
            gds.LoadFromEmbeddedResource();
            int version = gds.GetGameArtVersion();
            Directory.CreateDirectory(outDir);
            // Java: writerWithDefaultPrettyPrinter + Map.of("version", v) → { "version" : N }；
            // JsonNode + WriteIndented → { "version": N }（冒号前无空格），解析等价。
            var node = new JsonObject { ["version"] = version };
            File.WriteAllText(Path.Combine(outDir, VersionFile), node.ToJsonString(Indented));
        }
        catch (Exception e)
        {
            Log.LogWarning("Could not write version file: {Message}", e.Message);
        }
    }

    // Files.copy(REPLACE_EXISTING) → ExtractToFile(overwrite: true)
    private static void ExtractTo(ZipArchiveEntry entry, string outDir, string name)
    {
        string target = Path.Combine(outDir, name);
        string? dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        entry.ExtractToFile(target, overwrite: true);
    }
}
