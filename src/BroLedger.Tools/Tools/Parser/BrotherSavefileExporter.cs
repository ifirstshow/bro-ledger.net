using System.Globalization;
using System.Text;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;

namespace BroLedger.Tools.Parser;

/**
 * Writes a TSV file with one row per brother. Each column represents a parsed
 * segment; the first row is a marker/header row naming every column.
 *
 * Variable-length sections (inventory, circles, traits) use indexed column names
 * such as inv[0], inv[1].data, circ[0], trt[0]. Brothers with fewer items leave
 * those columns blank, which lets a spreadsheet comparison show the divergence.
 *
 * Parsing is tolerant: when an unknown item ID is encountered the remaining item
 * columns for that brother show "ABORTED" and the fixed sections that follow
 * (name, level, stars…) are left blank rather than crashing.
 *
 * Run from repo root:
 *   dotnet run --project src/BroLedger.Tools -- savefile save.sav output.tsv
 */
public class BrotherSavefileExporter
{
    private const string Wildman   = "6DF381C6";
    private const string Barbarian = "CB90AA90";

    private readonly DictionaryService _dict;
    private readonly ItemParser        _itemParser;

    public BrotherSavefileExporter(DictionaryService dict)
    {
        _dict       = dict;
        _itemParser = new ItemParser(dict, []);
    }

    // ---- entry point -------------------------------------------------------

    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: BrotherSavefileExporter <save.sav> <output.tsv>");
            return 1;
        }
        DictionaryService dict = DictionaryService.Instance;
        dict.LoadFromEmbeddedResource();
        new BrotherSavefileExporter(dict).Write(args[0], args[1]);
        return 0;
    }

    // ---- public API --------------------------------------------------------

    public void Write(string savePath, string outputPath)
    {
        byte[] bytes = File.ReadAllBytes(savePath);
        string hex   = HexUtils.ToHex(bytes);

        List<int> offsets = BrotherLocator.FindOffsets(hex);
        if (offsets.Count == 0)
        {
            Console.Error.WriteLine("BrotherSavefileExporter: brother signature not found");
            return;
        }

        // Pass 1 — parse each brother into an ordered list of (label, value) steps
        var allTraces = new List<List<string[]>>(offsets.Count);
        foreach (int offset in offsets)
        {
            var reader = new HexReader(hex, offset);
            var trace  = new List<string[]>();
            try
            {
                TraceParse(hex, reader, trace);
            }
            catch (Exception e)
            {
                trace.Add(Step("PARSE_EXCEPTION", e.GetType().Name + ": " + e.Message));
            }
            allTraces.Add(trace);
        }

        // Pass 2 — collect all unique labels in first-appearance order
        // (LinkedHashSet 语义：插入序 + 去重)
        var allLabels = new List<string>();
        var seen      = new HashSet<string>();
        foreach (List<string[]> trace in allTraces)
            foreach (string[] s in trace)
                if (seen.Add(s[0]))
                    allLabels.Add(s[0]);

        // Pass 3 — write TSV
        var tsv = new StringBuilder(1 << 20);

        // Marker / header row
        tsv.Append("brother");
        foreach (string lbl in allLabels) tsv.Append('\t').Append(lbl);
        tsv.Append('\n');

        for (var i = 0; i < allTraces.Count; i++)
        {
            List<string[]> trace = allTraces[i];
            // Merge duplicate labels (shouldn't happen for normal columns)
            var row = new Dictionary<string, string>();
            foreach (string[] s in trace)
            {
                if (row.TryGetValue(s[0], out string? prev))
                    row[s[0]] = prev + " | " + s[1];
                else
                    row[s[0]] = s[1];
            }

            string name = row.GetValueOrDefault("name", "#" + i);
            tsv.Append(i).Append(':').Append(name);
            foreach (string lbl in allLabels)
                tsv.Append('\t').Append(row.GetValueOrDefault(lbl, ""));
            tsv.Append('\n');
        }

        File.WriteAllText(outputPath, tsv.ToString()); // UTF-8 无 BOM
        Console.WriteLine("BrotherSavefileExporter: wrote " + Path.GetFullPath(outputPath)
                + " (" + allTraces.Count + " brothers, " + allLabels.Count + " columns)");
    }

    // ---- trace parse -------------------------------------------------------

    private void TraceParse(string hex, HexReader r, List<string[]> output)
    {

        // --- Visual layers ---
        int layerCount = r.ReadUInt8();
        output.Add(Step("layerCnt", layerCount.ToString()));
        for (var i = 0; i < layerCount; i++)
        {
            string key   = r.ReadString();
            string val12 = r.ReadHexBytes(12);
            r.Skip(53); // fixed separator
            output.Add(Step("lyr[" + i + "].key",   key));
            output.Add(Step("lyr[" + i + "].val12", val12));
        }

        // --- Header (member-count + variable blob) ---
        int hdrStart    = r.Cursor;
        int memberCount = r.ReadInt16LE();
        for (var i = 0; i < memberCount; i++)
        {
            int nameLen = r.ReadInt16LE();
            r.Skip(nameLen);
            string mt = r.ReadHexBytes(1);
            if      (mt == "02") r.Skip(1);
            else if (mt == "03") { int dl = r.ReadUInt16LE(); r.Skip(dl); }
            else                 r.Skip(4);
        }
        output.Add(Step("hdr", memberCount + "mbrs/" + (r.Cursor - hdrStart) / 2 + "B"));

        // --- Action points + 8 base stats ---
        output.Add(Step("ap", r.ReadUInt8().ToString()));
        string[] statNames = { "hp", "fat", "res", "mel", "rng", "def", "ini", "mor" };
        foreach (string sn in statNames) output.Add(Step(sn, r.ReadInt16LE().ToString()));

        // --- Misc ---
        output.Add(Step("greed",   r.ReadHexBytes(6)));
        output.Add(Step("pouches", r.ReadUInt8().ToString()));

        // --- Inventory ---
        int invCount = r.ReadUInt8();
        output.Add(Step("inv.cnt", invCount.ToString()));

        for (var i = 0; i < invCount; i++)
        {
            int    slotIdx = r.ReadUInt8();
            string itemId  = r.ReadHexBytes(4);
            string name    = _dict.GetName(itemId);
            string type    = _dict.Has(itemId) ? _dict.GetType(itemId)! : "?";

            output.Add(Step("inv[" + i + "]", "s" + slotIdx + "=" + itemId + "(" + name + ")"));

            if (!_dict.Has(itemId))
            {
                output.Add(Step("inv[" + i + "].data", "ABORTED-unknown-id"));
                return; // cannot recover: item byte length is unknown
            }

            int dataStart = r.Cursor;
            var slot = new InventorySlot(slotIdx)
            {
                ItemId   = itemId,
                ItemType = type,
            };
            _itemParser.Parse(r, slot);
            string dataHex = hex[dataStart..r.Cursor].ToUpperInvariant();
            output.Add(Step("inv[" + i + "].data", dataHex));
        }

        // --- Circles: perks + background ---
        int circleCount = r.ReadUInt16LE();
        output.Add(Step("circ.cnt", circleCount.ToString()));

        string? backgroundHexId = null;
        int circlesRead = 0;
        for (var i = 0; i < circleCount; i++)
        {
            string hexId  = r.ReadHexBytes(4);
            r.Skip(1); // isNew byte
            string ctype  = _dict.Has(hexId) ? _dict.GetType(hexId)! : "?";
            string cname  = _dict.GetName(hexId);
            output.Add(Step("circ[" + i + "]", hexId + "(" + ctype + ":" + cname + ")"));
            circlesRead++;
            if (ctype == "background")
            {
                backgroundHexId = hexId;
                break;
            }
        }

        // --- Description strings + salary ---
        output.Add(Step("desc",    Trunc(r.ReadString(), 50)));
        output.Add(Step("descTpl", Trunc(r.ReadString(), 40)));
        output.Add(Step("unk2",    r.ReadHexBytes(2)));
        output.Add(Step("salary",  r.ReadFloatLE().ToString("0.0000", CultureInfo.InvariantCulture)));

        // --- Tattoo (wildman / barbarian only) ---
        if (Wildman == backgroundHexId || Barbarian == backgroundHexId)
        {
            output.Add(Step("tattoo", r.ReadHexBytes(1)));
        }

        // --- Traits (remaining circle entries after perks + background) ---
        int traitIdx = 0;
        for (var i = circlesRead; i < circleCount; i++)
        {
            string traitId = r.ReadHexBytes(4);
            string ttype   = _dict.Has(traitId) ? _dict.GetType(traitId)! : "?";
            string tname   = _dict.GetName(traitId);

            if (ttype != "injury") r.Skip(1); // isNew byte

            int extraBytes = ttype switch
            {
                "injury"    => 14,
                "training"  => 37,
                "knowledge" => 12,
                "learning"  => 2,
                _           => 0,
            };
            string extraHex = extraBytes > 0 ? r.ReadHexBytes(extraBytes) : "";

            string cell = traitId + "(" + ttype + ":" + tname + ")"
                    + (extraHex.Length == 0 ? "" : "/" + extraHex);
            output.Add(Step("trt[" + traitIdx++ + "]", cell));
        }

        // --- Name / title / wounds / experience ---
        output.Add(Step("name",   r.ReadString()));
        output.Add(Step("title",  r.ReadString()));
        output.Add(Step("lwound", r.ReadFloatLE().ToString("0.0000", CultureInfo.InvariantCulture)));
        output.Add(Step("xp",     r.ReadUInt32LE().ToString()));

        // --- Human bytes + level data ---
        output.Add(Step("humanB", r.ReadHexBytes(6)));
        output.Add(Step("lvl",    r.ReadUInt8().ToString()));
        output.Add(Step("pp",     r.ReadUInt8().ToString()));
        output.Add(Step("pu",     r.ReadUInt8().ToString()));
        output.Add(Step("lp",     r.ReadUInt8().ToString()));
        output.Add(Step("morale", r.ReadFloatLE().ToString("0.0000", CultureInfo.InvariantCulture)));

        // --- Morale modifier blob ---
        int modCount = r.ReadUInt8();
        for (var i = 0; i < modCount; i++)
        {
            r.Skip(1);
            int textLen = r.ReadUInt16LE();
            r.Skip(textLen + 4);
        }
        r.Skip(8); // trailing bytes
        output.Add(Step("moraleMods", modCount.ToString()));

        // --- Stars ---
        var stars = new StringBuilder(16);
        for (var i = 0; i < 8; i++) { if (i > 0) stars.Append(','); stars.Append(r.ReadUInt8()); }
        output.Add(Step("stars", stars.ToString()));

        // --- Talents ---
        var talents = new StringBuilder(128);
        for (var i = 0; i < 8; i++)
        {
            int    tp = r.ReadUInt8();
            string tb = r.ReadHexBytes(tp);
            if (i > 0) talents.Append('|');
            talents.Append(tb.Length == 0 ? "-" : tb);
        }
        output.Add(Step("talents", talents.ToString()));
    }

    // ---- helpers -----------------------------------------------------------

    private static string[] Step(string label, string value)
    {
        return [label, value];
    }

    private static string Trunc(string s, int max)
    {
        return s.Length > max ? s[..max] + "…" : s;
    }
}
