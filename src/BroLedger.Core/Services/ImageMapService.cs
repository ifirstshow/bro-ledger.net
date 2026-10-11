using System.Text.Json;
using System.Text.Json.Nodes;

namespace BroLedger.Core.Services;

/// <summary>
/// Resolves a displayable item to a game-art relative path using the
/// image-map (imageSlots node) produced by the asset generator.
///
/// Resolution priority (first match wins):
///  1. slotHexIcon[slot:hexId:icon]  — named-item icon override
///  2. hexDirect[hexId]              — perks / traits / backgrounds / direct hex mappings
///  3. slotIcon[slot:icon:iconSet]   — slot+icon with iconSet qualifier
///  4. slotIcon[slot:icon]           — generic slot+icon
///  5. null (caller should use placeholder)
/// </summary>
public sealed class ImageMapService
{
    private static ImageMapService? _instance;
    public static ImageMapService Instance => _instance ??= new();

    private Dictionary<string, string> _hexDirect = [];
    private Dictionary<string, string> _slotIcon = [];
    private Dictionary<string, string> _slotHexIcon = [];
    private Dictionary<string, string> _slotHexHouse = [];
    private Dictionary<string, string> _slotAttachment = [];

    private ImageMapService() { }

    public void LoadFromEmbeddedResource()
    {
        var gds = GameDataService.Instance;
        gds.LoadFromEmbeddedResource();
        LoadFromGameData(gds.GetEntries(), gds.GetImageSlots());
    }

    /// <summary>
    /// Populates all maps from the unified game-data structure.
    /// All five maps come verbatim from the <c>imageSlots</c> node.
    /// Java parity: entriesNode is accepted but not read — do not "fix".
    /// </summary>
    internal void LoadFromGameData(JsonNode? entriesNode, JsonNode? imageSlotsNode)
    {
        _hexDirect = ToMap(Get(imageSlotsNode, "hexDirect"));
        _slotIcon = ToMap(Get(imageSlotsNode, "slotIcon"));
        _slotHexIcon = ToMap(Get(imageSlotsNode, "slotHexIcon"));
        _slotHexHouse = ToMap(Get(imageSlotsNode, "slotHexHouse"));
        _slotAttachment = ToMap(Get(imageSlotsNode, "slotAttachment"));
    }

    /// <summary>
    /// Loads from a <see cref="Stream"/> in the legacy image-map format
    /// (<c>{hexDirect:{…}, slotIcon:{…}, …}</c>). Used as a testing seam.
    /// </summary>
    public void Load(Stream json)
    {
        var root = JsonNode.Parse(json);
        _hexDirect = ToMap(Get(root, "hexDirect"));
        _slotIcon = ToMap(Get(root, "slotIcon"));
        _slotHexIcon = ToMap(Get(root, "slotHexIcon"));
        _slotHexHouse = ToMap(Get(root, "slotHexHouse"));
        _slotAttachment = ToMap(Get(root, "slotAttachment"));
    }

    /// <summary>Resolve an equipped item to a game-art path, or null if not mapped.</summary>
    public string? Resolve(string? slot, string? hexId, int icon, string? iconSet)
    {
        if (slot != null && hexId != null)
        {
            var byHexIcon = _slotHexIcon.GetValueOrDefault($"{slot}:{hexId}:{icon}");
            if (byHexIcon != null) return byHexIcon;
        }
        if (hexId != null)
        {
            var direct = _hexDirect.GetValueOrDefault(hexId);
            if (direct != null) return direct;
        }
        if (slot != null)
        {
            if (iconSet != null)
            {
                var withSet = _slotIcon.GetValueOrDefault($"{slot}:{icon}:{iconSet}");
                if (withSet != null) return withSet;
            }
            return _slotIcon.GetValueOrDefault($"{slot}:{icon}");
        }
        return null;
    }

    /// <summary>Resolve perks, traits, and backgrounds (hex-direct only).</summary>
    public string? ResolveHex(string? hexId)
    {
        if (hexId == null) return null;
        return _hexDirect.GetValueOrDefault(hexId.ToUpperInvariant());
    }

    /// <summary>Resolve an armor attachment overlay.</summary>
    public string? ResolveAttachment(string? slot, string? attachmentHexId)
    {
        if (slot == null || attachmentHexId == null) return null;
        return _slotAttachment.GetValueOrDefault($"{slot}:{attachmentHexId.ToUpperInvariant()}");
    }

    /// <summary>Resolve faction armor by slot + item hex ID + house index.</summary>
    public string? ResolveHexHouse(string? slot, string? hexId, int house)
    {
        if (slot == null || hexId == null) return null;
        return _slotHexHouse.GetValueOrDefault($"{slot}:{hexId.ToUpperInvariant()}:{house}");
    }

    // Java JsonNode.get(key) semantics: non-object node (or JSON null) → null → empty map.
    private static JsonNode? Get(JsonNode? node, string key) =>
        node is JsonObject obj ? obj[key] : null;

    private static Dictionary<string, string> ToMap(JsonNode? node)
    {
        if (node == null || node.GetValueKind() == JsonValueKind.Null) return [];
        return node.Deserialize<Dictionary<string, string>>() ?? [];
    }
}
