using System.Text.Json;
using System.Text.Json.Nodes;
using BroLedger.Core.Services;
using Json.Schema;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// 每次构建校验 game-data.json 与 schema 一致，捕获宽容解析器会吞掉的坏条目
public class GameDataSchemaTests
{
    private const string DataBase = "BroLedger.Core.Resources.data.";

    [Fact]
    public void GameData_ConformsToSchema()
    {
        var asm = typeof(GameDataService).Assembly;

        JsonNode schemaNode, dataNode;
        using (var s = asm.GetManifestResourceStream(DataBase + "game-data.schema.json"))
        {
            Assert.NotNull(s);
            schemaNode = JsonNode.Parse(s)!;
        }
        using (var s = asm.GetManifestResourceStream(DataBase + "game-data.json"))
        {
            Assert.NotNull(s);
            dataNode = JsonNode.Parse(s)!;
        }

        var schema = JsonSchema.FromText(schemaNode.ToJsonString());
        // 9.x 移除了 Evaluate(JsonNode) 重载，经 JsonElement 等价传入
        using var dataDoc = JsonDocument.Parse(dataNode.ToJsonString());
        var results = schema.Evaluate(dataDoc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!results.IsValid)
        {
            var details = results.Details!
                .Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Values)
                .Take(20);
            Assert.Fail($"{results.Details!.Count(d => d.Errors != null)} schema violation(s):\n  {string.Join("\n  ", details)}");
        }
    }
}
