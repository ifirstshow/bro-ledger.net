using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BroLedger.Core.Utils;

public static class HexUtils
{
    public static byte[] FromHex(string hex)
    {
        var len = hex.Length;
        var data = new byte[len / 2];
        for (var i = 0; i < len; i += 2)
            data[i / 2] = (byte)((Uri.FromHex(hex[i]) << 4) + Uri.FromHex(hex[i + 1]));
        return data;
    }

    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    public static short Int16LE(string hex2bytes) =>
        BinaryPrimitives.ReadInt16LittleEndian(FromHex(hex2bytes));

    public static int UInt16LE(string hex2bytes) =>
        BinaryPrimitives.ReadUInt16LittleEndian(FromHex(hex2bytes));

    public static float FloatLE(string hex4bytes) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(FromHex(hex4bytes)));

    public static uint UInt32LE(string hex4bytes) =>
        BinaryPrimitives.ReadUInt32LittleEndian(FromHex(hex4bytes));

    public static int UInt8(string hex1byte) => FromHex(hex1byte)[0];

    public static int Int8(string hex1byte) => (sbyte)FromHex(hex1byte)[0];

    public static string ToHexLower(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    public static string UInt16ToHexLE(int value) =>
        $"{value & 0xFF:X2}{(value >> 8) & 0xFF:X2}";

    public static int HexCharSpan(int byteCount) => byteCount * 2;

    public static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
