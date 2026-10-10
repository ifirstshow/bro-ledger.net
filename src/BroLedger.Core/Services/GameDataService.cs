using System.Text.Json.Nodes;

namespace BroLedger.Core.Services;

public sealed class GameDataService
{
    private static GameDataService? _instance;
    public static GameDataService Instance => _instance ??= new();

    private JsonNode? _root;

    private GameDataService() { }

    // 幂等：已加载则后续调用为空操作
    public void LoadFromEmbeddedResource()
    {
        if (_root != null) return;
        using var s = typeof(GameDataService).Assembly
            .GetManifestResourceStream("BroLedger.Core.Resources.data.game-data.json")
            ?? throw new IOException("game-data.json not found in embedded resources");
        _root = JsonNode.Parse(s);
    }

    public JsonNode? GetEntries() => SafeGet("entries");

    public JsonNode? GetImageSlots() => SafeGet("imageSlots");

    public int GetGameArtVersion()
    {
        var node = SafeGet("gameArtVersion");
        return node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    }

    private JsonNode? SafeGet(string field) => _root?[field];
}
