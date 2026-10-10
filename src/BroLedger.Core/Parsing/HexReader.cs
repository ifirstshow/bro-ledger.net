using System.Text;
using BroLedger.Core.Utils;

namespace BroLedger.Core.Parsing;

// 基于 hex 字符串的游标读取器；每次读取前进 byteCount*2 个字符
public class HexReader(string data, int startOffset)
{
    public int Cursor { get; set; } = startOffset;

    public void Skip(int byteCount) => Cursor += HexUtils.HexCharSpan(byteCount);

    // 读取 byteCount 字节，返回大写 hex 字符串
    public string ReadHexBytes(int byteCount)
    {
        var span = HexUtils.HexCharSpan(byteCount);
        var result = data.Substring(Cursor, span).ToUpperInvariant();
        Cursor += span;
        return result;
    }

    // 在绝对字符偏移处窥视，不移动游标
    public string PeekAt(int absoluteCharOffset, int byteCount) =>
        data.Substring(absoluteCharOffset, HexUtils.HexCharSpan(byteCount)).ToUpperInvariant();

    public int ReadUInt8() => HexUtils.UInt8(ReadHexBytes(1));

    public int ReadInt8() => HexUtils.Int8(ReadHexBytes(1));

    public short ReadInt16LE() => HexUtils.Int16LE(ReadHexBytes(2));

    public int ReadUInt16LE() => HexUtils.UInt16LE(ReadHexBytes(2));

    public float ReadFloatLE() => HexUtils.FloatLE(ReadHexBytes(4));

    public uint ReadUInt32LE() => HexUtils.UInt32LE(ReadHexBytes(4));

    // 长度前缀 UTF-8 字符串：UInt16LE 长度 + 该长度的 UTF-8 字节
    public string ReadString()
    {
        var len = ReadUInt16LE();
        if (len == 0) return "";
        var bytes = HexUtils.FromHex(ReadHexBytes(len));
        return Encoding.UTF8.GetString(bytes);
    }
}
