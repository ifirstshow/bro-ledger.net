using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Parsing;

public class SaveParser
{
    private readonly DictionaryService _dict;
    private readonly ILogger _logger;
    private readonly List<string> _warnings = [];

    public SaveParser(DictionaryService dict, ILogger? logger = null)
    {
        _dict = dict;
        _logger = logger ?? NullLogger.Instance;
    }

    // 最近一次解析收集的警告（未知物品类型等）
    public IReadOnlyList<string> Warnings => _warnings;

    public List<Brother> Parse(string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        // 转小写 hex 字符串——与 Node fs.readFile(path, "hex") 一致
        var hex = HexUtils.ToHexLower(bytes);
        return ParseHex(hex);
    }

    public List<Brother> ParseHex(string hex)
    {
        _warnings.Clear();
        var brotherParser = new BrotherParser(_dict, _warnings, _logger);
        var brothers = new List<Brother>();

        var offsets = BrotherLocator.FindOffsets(hex);
        if (offsets.Count == 0)
        {
            _logger.LogWarning("SaveParser: brother signature not found");
            return brothers;
        }

        foreach (var offset in offsets)
        {
            try
            {
                var reader = new HexReader(hex, offset);
                var b = brotherParser.Parse(reader, hex);
                brothers.Add(b);
            }
            catch (Exception e)
            {
                _logger.LogWarning("SaveParser: failed to parse brother at offset {Offset}: {Message}",
                    offset, e.Message);
            }
        }

        return brothers;
    }
}
