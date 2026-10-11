using System.Text;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;

namespace BroLedger.Tools;

/**
 * Exports all brothers from the last-loaded save file to a semicolon-separated CSV.
 * Output is written next to the save file using the same base name (e.g. autosave.csv).
 *
 * Run:
 *   dotnet run --project src/BroLedger.Tools -- csv
 * Optional output path argument:
 *   dotnet run --project src/BroLedger.Tools -- csv C:\path\to\output.csv
 *
 * Ported line-by-line from JAVA/src/main/java/.../tools/BrotherCsvExporter.java.
 */
public class BrotherCsvExporter
{
    private const string Sep = ";";

    private static readonly Dictionary<string, int> TraitTypeOrder = new()
    {
        ["training"]  = 0,
        ["knowledge"] = 1,
        ["learning"]  = 2,
        ["injury"]    = 3,
    };

    private readonly DictionaryService   _dict;
    private readonly StatModifierService _mods;
    private readonly AnnotationService   _annotations;
    private readonly RoleService         _roles;

    public BrotherCsvExporter(DictionaryService dict, StatModifierService mods,
                              AnnotationService annotations, RoleService roles)
    {
        _dict        = dict;
        _mods        = mods;
        _annotations = annotations;
        _roles       = roles;
    }

    // ---- entry point -------------------------------------------------------

    public static int Run(string[] args)
    {
        AppConfig config = AppConfig.Instance;
        config.Load();

        string? savePath = config.LastSaveFilePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            Console.Error.WriteLine("BrotherCsvExporter: no last save file in ~/.bro-ledger/config.json — open the app once to set it");
            return 1;
        }

        DictionaryService dict = DictionaryService.Instance;
        dict.LoadFromEmbeddedResource();
        StatModifierService mods = StatModifierService.Instance;
        mods.LoadFromEmbeddedResource();

        string outputPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetDirectoryName(savePath) ?? "", BaseName(savePath) + ".csv");

        new BrotherCsvExporter(dict, mods, AnnotationService.Instance, RoleService.Instance)
                .Export(config, savePath, outputPath);
        return 0;
    }

    // ---- public API --------------------------------------------------------

    public void Export(AppConfig config, string savePath, string outputPath)
    {
        SaveParser parser = new(_dict);
        List<Brother> brothers = parser.Parse(savePath);

        if (brothers.Count == 0)
        {
            Console.Error.WriteLine("BrotherCsvExporter: no brothers found in " + savePath);
            return;
        }

        _annotations.LoadFor(savePath);
        Stat[] statOrder = config.OrderedStats();

        // Sort each brother's perks once; track the max column count for the header.
        List<List<string>> allPerkNames = brothers
                .Select(SortedPerkNames)
                .ToList();
        int maxPerks = allPerkNames.Count == 0 ? 0 : allPerkNames.Max(p => p.Count);

        using (var w = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
        {
            w.WriteLine(BuildHeader(statOrder, maxPerks));
            for (var i = 0; i < brothers.Count; i++)
            {
                w.WriteLine(BuildRow(brothers[i], statOrder, allPerkNames[i], maxPerks));
            }
        }

        Console.WriteLine("BrotherCsvExporter: wrote " + brothers.Count + " brothers to " + Path.GetFullPath(outputPath));
        foreach (string warn in parser.Warnings)
        {
            Console.WriteLine("  warn: " + warn);
        }
    }

    // ---- row building ------------------------------------------------------

    private string BuildHeader(Stat[] statOrder, int maxPerks)
    {
        var cols = new List<string?>
        {
            "Name",
            "Title",
            "Role",
            "Background",
            "Level",
            "XP",
        };
        foreach (Stat s in statOrder)
        {
            cols.Add(s.Abbrev);
            cols.Add(s.Abbrev + "*");
            cols.Add(s.Abbrev + " pot");
        }
        for (var i = 1; i <= maxPerks; i++) cols.Add("Perk " + i);
        cols.Add("Traits");
        return ToLine(cols);
    }

    private string BuildRow(Brother b, Stat[] statOrder, List<string> perkNames, int maxPerks)
    {
        var cols = new List<string?>
        {
            b.Name,
            b.Title,
        };
        string? roleId = _annotations.Get(b.Fingerprint!).RoleId;
        Role? role = roleId != null ? _roles.GetById(roleId) : null;
        cols.Add(roleId != null && role != null ? role.Name : "");
        cols.Add(_dict.GetName(b.BackgroundHexId!));
        cols.Add(b.LevelTotal.ToString());
        cols.Add(b.Experience.ToString());
        foreach (Stat s in statOrder)
        {
            cols.Add(b.Stats[s.StatIndex].ToString());
            cols.Add(new string('*', b.Stars[s.StarIndex]));
            cols.Add(StatPotentialCalculator.Compute(b, s).FinalPotential.ToString());
        }
        for (var i = 0; i < maxPerks; i++) cols.Add(i < perkNames.Count ? perkNames[i] : "");
        cols.Add(FormatTraits(b));
        return ToLine(cols);
    }

    // ---- formatting --------------------------------------------------------

    /** Perks sorted by tier (ascending, untiered last), then by name within each tier. */
    private List<string> SortedPerkNames(Brother b)
    {
        return b.PerkIds
                .OrderBy(id => OverviewCalc.TierOrMax(_mods.GetTier(id)))
                .ThenBy(_dict.GetName, StringComparer.Ordinal)
                .Select(id => OverviewCalc.DecoratePerkName(_mods.GetTier(id), _dict.GetName(id)))
                .ToList();
    }

    /** Traits sorted by type order (training → knowledge → learning → injury), then by name.
     *  Entries not found in the dictionary are excluded. */
    private string FormatTraits(Brother b)
    {
        return string.Join("; ", b.Traits
                .Where(t => _dict.Has(t.Id))
                .OrderBy(t => TraitTypeOrder.GetValueOrDefault(_dict.GetType(t.Id)!, 99))
                .ThenBy(t => _dict.GetName(t.Id), StringComparer.Ordinal)
                .Select(t => _dict.GetName(t.Id)));
    }

    // ---- helpers -----------------------------------------------------------

    private static string BaseName(string path)
    {
        string name = Path.GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string ToLine(IEnumerable<string?> values)
    {
        return string.Join(Sep, values.Select(Sanitize));
    }

    /** Strips characters that would break CSV structure. */
    private static string? Sanitize(string? value)
    {
        if (value == null) return "";
        return value.Replace(";", " ").Replace("\n", " ").Replace("\r", "");
    }
}
