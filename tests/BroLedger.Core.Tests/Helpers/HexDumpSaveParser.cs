using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;

namespace BroLedger.Core.Tests.Helpers;

// 包装 SaveParser：解析存档并把每个兄弟的原始 hex 段写到工作目录 <savename>_brother_<i>.hex
// （128 字节/行 + 下一行 ASCII 对照；不可打印字节显示 '.'）
public class HexDumpSaveParser
{
    private const int BytesPerLine = 128;

    private readonly DictionaryService _dict;

    public HexDumpSaveParser(DictionaryService dict) => _dict = dict;

    public List<Brother> Parse(string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        var hex = HexUtils.ToHex(bytes);

        var fileName = Path.GetFileName(savePath);
        var dot = fileName.LastIndexOf('.');
        var baseName = dot > 0 ? fileName[..dot] : fileName;

        return ParseAndDump(hex, baseName);
    }

    internal static string FormatHexDump(string hex)
    {
        var charsPerLine = BytesPerLine * 2;
        var sb = new System.Text.StringBuilder(hex.Length * 2);

        for (var pos = 0; pos < hex.Length; pos += charsPerLine)
        {
            var lineEnd = Math.Min(pos + charsPerLine, hex.Length);
            var hexLine = hex[pos..lineEnd];
            sb.Append(hexLine).Append('\n');

            var byteCount = hexLine.Length / 2;
            for (var i = 0; i < byteCount; i++)
            {
                var b = Convert.ToInt32(hexLine.Substring(i * 2, 2), 16);
                var c = b is >= 0x20 and <= 0x7E ? (char)b : '.';
                sb.Append(' ').Append(c);
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private List<Brother> ParseAndDump(string hex, string baseName)
    {
        var brotherParser = new BrotherParser(_dict, []);
        var brothers = new List<Brother>();

        var parseOffsets = BrotherLocator.FindOffsets(hex);
        if (parseOffsets.Count == 0)
        {
            Console.Error.WriteLine("HexDumpSaveParser: brother signature not found");
            return brothers;
        }

        var sigStarts = parseOffsets.Select(o => o - BrotherLocator.SigDataAhead).ToList();

        // 解析长度转储 (_brother_N.hex)
        for (var i = 0; i < parseOffsets.Count; i++)
        {
            var start = parseOffsets[i];
            var reader = new HexReader(hex, start);
            Brother? b = null;
            try
            {
                b = brotherParser.Parse(reader, hex);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"HexDumpSaveParser: failed parsing brother {i} at offset {start}: {e.Message}");
            }
            finally
            {
                var outFile = Path.Combine(Environment.CurrentDirectory, $"{baseName}_brother_{i}.hex");
                File.WriteAllText(outFile, FormatHexDump(hex[start..reader.Cursor]));
                Console.WriteLine($"HexDumpSaveParser: wrote {Path.GetFullPath(outFile)}");
            }
            if (b != null) brothers.Add(b);
        }

        // 签名到签名转储 (_brother_NA.hex)
        var sigCount = sigStarts.Count;
        var maxSigLength = 0;
        for (var i = 0; i < sigCount - 1; i++)
            maxSigLength = Math.Max(maxSigLength, sigStarts[i + 1] - sigStarts[i]);

        for (var i = 0; i < sigCount; i++)
        {
            var start = sigStarts[i];
            var end = i < sigCount - 1 ? sigStarts[i + 1] : Math.Min(start + maxSigLength, hex.Length);

            var outFile = Path.Combine(Environment.CurrentDirectory, $"{baseName}_brother_{i}A.hex");
            File.WriteAllText(outFile, FormatHexDump(hex[start..end]));
            Console.WriteLine($"HexDumpSaveParser: wrote {Path.GetFullPath(outFile)}");
        }

        return brothers;
    }
}
