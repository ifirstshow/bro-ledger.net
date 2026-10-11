using System.Globalization;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;

namespace BroLedger.Tools;

/**
 * Replays the snapshot sequence captured by SaveReplayService and prints detected
 * changes (stat deltas, perks, level-ups) plus adjustments (planned-increase consumption)
 * for each transition between consecutive save snapshots.
 *
 * Usage:
 *   dotnet run --project src/BroLedger.Tools -- replay [<save.sav>|--dir <dir>] [<transition>] [<output.txt>]
 *
 *   <save.sav>   — path to the live save; the tool resolves its replay directory.
 *                — or --dir <replayDir> to point directly at a replay directory.
 *                — omit to select interactively.
 *   <transition> — 1-based index, or "all" (default when a save path is given).
 *                — omit to select interactively.
 *   <output>     — optional output file path; stdout if omitted.
 *
 * Ported line-by-line from JAVA/src/main/java/.../tools/ReplayAnalyzer.java.
 * Visibility mapping: Java package-private → internal (tests use InternalsVisibleTo).
 */
public static class ReplayAnalyzer
{
    /** A consecutive pair of snapshot files to analyze. */
    internal sealed record Transition(int Idx, string Prev, string Next);

    /** Display format for snapshot timestamps parsed from filename stems. Java "SSS" → .NET "fff". */
    private const string DisplayFmt = "yyyy-MM-dd HH:mm:ss.fff 'UTC'";

    /** Parse format matching SaveReplayService.TsFmt (the snapshot filename pattern). */
    private const string ParseFmt = "yyyyMMdd'T'HHmmss'_'fff'Z'";

    // ---- entry point -------------------------------------------------------

    public static int Run(string[] args)
    {
        DictionaryService dict = DictionaryService.Instance;
        dict.LoadFromEmbeddedResource();
        SaveReplayService srs = SaveReplayService.Instance;

        // Parse positional args (all optional)
        var useDir = false;
        string? resolvedDir = null;
        string? savArg = null;
        string? transitionArg = null;
        string? outputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if ("--dir".Equals(args[i]) && i + 1 < args.Length)
            {
                useDir = true;
                resolvedDir = args[++i];
            }
            else if (savArg == null && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                savArg = args[i];
            }
            else if (transitionArg == null && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                transitionArg = args[i];
            }
            else if (outputPath == null && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                outputPath = args[i];
            }
        }

        // ---- resolve replay directory ----------------------------------------
        if (!useDir)
        {
            if (savArg != null)
            {
                resolvedDir = srs.ReplayDir(savArg);
            }
            else
            {
                resolvedDir = PickReplayDirInteractively(srs);
                if (resolvedDir == null)
                {
                    Console.Error.WriteLine("No replay data found. Enable 'Update Replay' in Preferences first.");
                    return 0;
                }
            }
        }

        // ---- load snapshots --------------------------------------------------
        List<string> snapshots = ListSavSnapshots(resolvedDir);
        if (snapshots.Count < 2)
        {
            Console.Error.WriteLine("Need at least 2 snapshots to form a transition (found "
                    + snapshots.Count + " in " + resolvedDir + ").");
            return 0;
        }

        var allTransitions = new List<Transition>();
        for (var i = 0; i < snapshots.Count - 1; i++)
        {
            allTransitions.Add(new Transition(i + 1, snapshots[i], snapshots[i + 1]));
        }

        // ---- resolve which transitions to analyze ----------------------------
        List<Transition>? toAnalyze;
        if (transitionArg != null)
        {
            toAnalyze = ResolveTransitionArg(transitionArg, allTransitions);
            if (toAnalyze == null)
            {
                Console.Error.WriteLine("Invalid transition argument: '" + transitionArg
                        + "'. Use a 1-based index (1.." + allTransitions.Count + ") or 'all'.");
                return 0;
            }
        }
        else if (savArg != null || useDir)
        {
            toAnalyze = allTransitions; // default to all when source is from args
        }
        else
        {
            toAnalyze = PickTransitionsInteractively(allTransitions);
            if (toAnalyze == null) return 0;
        }

        // ---- set up output ---------------------------------------------------
        TextWriter output = Console.Out;
        if (outputPath != null)
        {
            output = new StreamWriter(outputPath, append: false); // Java TRUNCATE_EXISTING
        }

        // ---- run analysis ----------------------------------------------------
        AnalyzeTransitions(toAnalyze, allTransitions, snapshots, dict, srs, output);

        if (outputPath != null)
        {
            output.Dispose();
            Console.WriteLine("Report written to " + Path.GetFullPath(outputPath));
        }
        return 0;
    }

    // ---- interactive helpers -----------------------------------------------

    private static string? PickReplayDirInteractively(SaveReplayService srs)
    {
        List<SaveReplayService.ReplayEntry> entries = srs.ListAll()
                .Where(e => e.SnapshotCount >= 2)
                .ToList();
        if (entries.Count == 0) return null;

        Console.WriteLine("Captured save files:");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            Console.WriteLine($"  [{i + 1}] {e.SourceSavePath}  ({e.SnapshotCount} snapshots)");
        }
        Console.Write("Select save file (1–" + entries.Count + "): ");

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            line = line.Trim();
            if (int.TryParse(line, out var choice) && choice >= 1 && choice <= entries.Count)
            {
                return entries[choice - 1].Dir;
            }
            Console.Write("Invalid choice. Enter a number 1–" + entries.Count + ": ");
        }
        return null;
    }

    private static List<Transition>? PickTransitionsInteractively(List<Transition> allTransitions)
    {
        Console.WriteLine("\nAvailable transitions:");
        foreach (var t in allTransitions)
        {
            Console.WriteLine($"  [{t.Idx}] {DisplayTs(StemOf(t.Prev))}  →  {DisplayTs(StemOf(t.Next))}");
        }
        Console.WriteLine("  [A] All transitions");
        Console.Write("Select transition (1–" + allTransitions.Count + " or A): ");

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            line = line.Trim();
            if ("a".Equals(line, StringComparison.OrdinalIgnoreCase)
                    || "all".Equals(line, StringComparison.OrdinalIgnoreCase))
            {
                return allTransitions;
            }
            if (int.TryParse(line, out var choice) && choice >= 1 && choice <= allTransitions.Count)
            {
                return [allTransitions[choice - 1]];
            }
            Console.Write("Invalid. Enter 1–" + allTransitions.Count + " or A: ");
        }
        return null;
    }

    private static List<Transition>? ResolveTransitionArg(string arg, List<Transition> all)
    {
        if ("all".Equals(arg, StringComparison.OrdinalIgnoreCase)) return all;
        if (int.TryParse(arg, out var idx) && idx >= 1 && idx <= all.Count)
        {
            return [all[idx - 1]];
        }
        return null;
    }

    // ---- core analysis -----------------------------------------------------

    private static void AnalyzeTransitions(
            List<Transition> toAnalyze,
            List<Transition> allTransitions,
            List<string> allSnapshots,
            DictionaryService dict,
            SaveReplayService srs,
            TextWriter output)
    {
        var analyzeAll = toAnalyze.Count == allTransitions.Count
                && toAnalyze.Count > 1;

        // Carried planned-increase state, updated across transitions in all-mode.
        var carriedPlanned = new Dictionary<string, int[]>();
        var carriedPost11 = new Dictionary<string, int[]>();
        var carriedInitialized = false;

        var totalLevelUps = 0;
        var totalAdjusted = 0;
        var totalAnomalies = 0;
        var transitionsDone = 0;

        var annotSvc = AnnotationService.Instance;

        foreach (var t in toAnalyze)
        {
            output.WriteLine();
            output.WriteLine(new string('=', 70));
            output.WriteLine($"Transition {t.Idx}/{allTransitions.Count}:  {DisplayTs(StemOf(t.Prev))}  →  {DisplayTs(StemOf(t.Next))}");
            output.WriteLine(new string('=', 70));

            // Parse both snapshots
            List<Brother> prevBrothers = ParseSave(t.Prev, dict);
            List<Brother> nextBrothers = ParseSave(t.Next, dict);

            // Pre-reconciliation planned increases for this transition are stored in
            // the annotations paired with t.Next (captured before reconciliation ran).
            string? nextAnnotPath = srs.AnnotationsFor(t.Next);
            var transPlanned = new Dictionary<string, int[]>();
            var transPost11 = new Dictionary<string, int[]>();
            AnnotationService.ReadAnnotationMaps(nextAnnotPath, transPlanned, transPost11);

            // Initialize carried state from the very first snapshot's annotations
            if (analyzeAll && !carriedInitialized)
            {
                string? prevAnnotPath = srs.AnnotationsFor(t.Prev);
                AnnotationService.ReadAnnotationMaps(prevAnnotPath, carriedPlanned, carriedPost11);
                carriedInitialized = true;
            }

            // ---- Roster changes -----------------------------------------------
            var anomalies = new List<string>();
            ReportRosterChanges(prevBrothers, nextBrothers, output, anomalies);

            // ---- Per-brother changes and adjustments ---------------------------
            // Use per-transition maps (accurate pre-state for this exact overwrite)
            var localPlanned = DeepCopy(transPlanned);
            var localPost11 = DeepCopy(transPost11);
            List<AnnotationService.LevelUpEvent> events =
                    annotSvc.PreviewReconcile(prevBrothers, nextBrothers, localPlanned, localPost11);

            if (events.Count == 0 && anomalies.Count == 0)
            {
                output.WriteLine("  (no changes detected)");
            }

            foreach (var e in events)
            {
                ReportLevelUpEvent(e, output, dict);
                totalLevelUps++;
                if (e.Adjusted) totalAdjusted++;
            }

            // ---- Anomaly detection --------------------------------------------
            DetectAnomalies(prevBrothers, nextBrothers, anomalies);

            // ---- Cross-check (all-mode only, when the next snapshot exists) ---
            if (analyzeAll)
            {
                // Advance carried state with our computed result
                var carriedCopy = DeepCopy(carriedPlanned);
                var carried11Copy = DeepCopy(carriedPost11);
                annotSvc.PreviewReconcile(prevBrothers, nextBrothers, carriedCopy, carried11Copy);

                // t.Idx is 1-based; allSnapshots[t.Idx] is the snapshot after t.Next
                var afterIdx = t.Idx; // e.g. idx=1 → allSnapshots[1] = second snapshot
                if (afterIdx < allSnapshots.Count)
                {
                    string afterSnap = allSnapshots[afterIdx];
                    string? afterAnnotPath = srs.AnnotationsFor(afterSnap);
                    var actualPlanned = new Dictionary<string, int[]>();
                    var actualPost11 = new Dictionary<string, int[]>();
                    AnnotationService.ReadAnnotationMaps(afterAnnotPath, actualPlanned, actualPost11);

                    List<string> crossIssues = CrossCheck(
                            nextBrothers, carriedCopy, carried11Copy, actualPlanned, actualPost11);
                    if (crossIssues.Count > 0)
                    {
                        output.WriteLine();
                        output.WriteLine("  *** RECONCILIATION CROSS-CHECK MISMATCHES ***");
                        crossIssues.ForEach(s => output.WriteLine("  ! " + s));
                        anomalies.AddRange(crossIssues);
                    }
                }

                // Persist carried state for next iteration
                carriedPlanned.Clear();
                foreach (var (k, v) in carriedCopy) carriedPlanned[k] = v;
                carriedPost11.Clear();
                foreach (var (k, v) in carried11Copy) carriedPost11[k] = v;
            }

            // ---- Print anomalies ----------------------------------------------
            if (anomalies.Count > 0)
            {
                output.WriteLine();
                output.WriteLine("  Anomalies:");
                anomalies.ForEach(s => output.WriteLine("    ! " + s));
                totalAnomalies += anomalies.Count;
            }

            transitionsDone++;
        }

        // ---- Summary ----------------------------------------------------------
        output.WriteLine();
        output.WriteLine(new string('=', 70));
        output.WriteLine($"Summary: {transitionsDone} transition(s) analyzed, {totalLevelUps} level-up event(s), "
                + $"{totalAdjusted} adjusted, {totalAnomalies} anomaly/anomalies");
        output.WriteLine(new string('=', 70));
    }

    // ---- report helpers ----------------------------------------------------

    internal static void ReportRosterChanges(
            List<Brother> prev, List<Brother> next,
            TextWriter output, List<string> anomalies)
    {
        var prevByFp = ByFingerprint(prev);
        var nextByFp = ByFingerprint(next);

        List<string> added = nextByFp.Keys
                .Where(fp => !prevByFp.ContainsKey(fp))
                .ToList();
        List<string> removed = prevByFp.Keys
                .Where(fp => !nextByFp.ContainsKey(fp))
                .ToList();

        if (added.Count > 0 || removed.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  Roster changes:");
            added.ForEach(fp => output.WriteLine("    + " + nextByFp[fp].Name + " (new)"));
            removed.ForEach(fp => output.WriteLine("    - " + prevByFp[fp].Name + " (removed)"));
        }

        if (prev.Count != next.Count)
        {
            anomalies.Add("Brother count changed: " + prev.Count + " → " + next.Count);
        }
    }

    internal static void ReportLevelUpEvent(AnnotationService.LevelUpEvent e, TextWriter output, DictionaryService dict)
    {
        output.WriteLine();
        output.WriteLine("  --- " + e.Name + " ---");

        if (e.LevelsAssigned > 0)
        {
            output.WriteLine("  Level-up: " + e.LevelsAssigned + " assigned"
                    + (e.Post11 ? " (post-11)" : ""));
        }

        if (e.StatDeltas.Count > 0)
        {
            string deltas = string.Join(", ",
                    e.StatDeltas.Select(en => en.Key.Abbrev + " +" + en.Value));
            output.WriteLine("  Stats:    " + deltas);
        }

        if (e.AddedPerkIds.Count > 0)
        {
            string perks = string.Join(", ",
                    e.AddedPerkIds.Select(id =>
                    {
                        var de = dict.Get(id);
                        return de != null ? de.Name : id;
                    }));
            output.WriteLine("  Perks:    " + perks);
        }

        if (e.Adjusted && e.ConsumedIncreases.Count > 0)
        {
            string adj = string.Join(", ",
                    e.ConsumedIncreases.Select(en => en.Key.Abbrev + ": " + en.Value + " consumed"));
            output.WriteLine("  Adjusted: " + adj);
        }
        else if (e.Adjusted && e.Post11)
        {
            output.WriteLine("  Adjusted: post-11 increases recorded");
        }
    }

    internal static void DetectAnomalies(
            List<Brother> prev, List<Brother> next, List<string> anomalies)
    {
        var prevByFp = ByFingerprint(prev);
        var nextByFp = ByFingerprint(next);

        foreach (var entry in nextByFp)
        {
            string fp = entry.Key;
            Brother nb = entry.Value;
            Brother? ob = prevByFp.GetValueOrDefault(fp);
            if (ob == null) continue;

            foreach (var s in Stat.Values)
            {
                var si = s.StatIndex;
                if (si >= ob.Stats.Length || si >= nb.Stats.Length) continue;
                var delta = nb.Stats[si] - ob.Stats[si];
                if (delta < 0)
                {
                    anomalies.Add(nb.Name + ": " + s.Abbrev + " decreased by " + (-delta));
                }
            }
        }
    }

    /// <summary>
    /// Compares our carried-forward reconciliation result against the actual on-disk state
    /// captured at the start of the next overwrite. Returns a (possibly empty) list of
    /// mismatch descriptions that indicate the live reconciler behaved differently.
    /// </summary>
    internal static List<string> CrossCheck(
            List<Brother> brothers,
            Dictionary<string, int[]> computedPlanned,
            Dictionary<string, int[]> computedPost11,
            Dictionary<string, int[]> actualPlanned,
            Dictionary<string, int[]> actualPost11)
    {
        var issues = new List<string>();
        foreach (var b in brothers)
        {
            string? fp = b.Fingerprint;
            if (fp == null) continue;
            CheckArrayMismatch(b.Name, "planned",
                    computedPlanned.GetValueOrDefault(fp), actualPlanned.GetValueOrDefault(fp), issues);
            CheckArrayMismatch(b.Name, "post11",
                    computedPost11.GetValueOrDefault(fp), actualPost11.GetValueOrDefault(fp), issues);
        }
        return issues;
    }

    private static void CheckArrayMismatch(
            string? name, string label,
            int[]? computed, int[]? actual,
            List<string> issues)
    {
        if (IsNullOrZero(computed) && IsNullOrZero(actual)) return;
        int[] c = NullToZero(computed);
        int[] a = NullToZero(actual);
        if (!c.SequenceEqual(a))
        {
            issues.Add(name + " " + label + " mismatch — computed: "
                    + ArrayToString(c) + "  actual: " + ArrayToString(a));
        }
    }

    // ---- parse helpers -----------------------------------------------------

    private static List<Brother> ParseSave(string savPath, DictionaryService dict)
    {
        try
        {
            return new SaveParser(dict).Parse(savPath)
                    .Where(IsValid)
                    .ToList();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("WARNING: parse failed for "
                    + Path.GetFileName(savPath) + " — " + e.Message);
            return [];
        }
    }

    /// <summary>Replicates MainController.isValid locally to avoid a UI dependency.</summary>
    internal static bool IsValid(Brother b)
    {
        if (b.Name == null || string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 60) return false;
        if (b.LevelTotal < 1) return false;
        if (b.BackgroundHexId == null) return false;
        DictionaryEntry? bg = DictionaryService.Instance.Get(b.BackgroundHexId);
        return bg != null && "background".Equals(bg.Type);
    }

    // ---- formatting helpers ------------------------------------------------

    /// <summary>
    /// Converts a snapshot filename stem (e.g. <c>20260613T120000_000Z</c>) to a
    /// human-readable UTC string. Returns the stem unchanged if parsing fails.
    /// </summary>
    internal static string DisplayTs(string? stem)
    {
        if (stem == null || stem.Length < 19) return stem ?? "";
        if (DateTimeOffset.TryParseExact(stem, ParseFmt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var ts))
        {
            return ts.ToString(DisplayFmt, CultureInfo.InvariantCulture);
        }
        return stem;
    }

    internal static string StemOf(string snapshot)
    {
        string name = Path.GetFileName(snapshot);
        return name.EndsWith(".sav", StringComparison.Ordinal) ? name[..^4] : name;
    }

    // ---- collection helpers ------------------------------------------------

    private static Dictionary<string, Brother> ByFingerprint(List<Brother> brothers)
    {
        // Java LinkedHashMap.put 覆盖同键值但保留首插位置；C# Dictionary indexer 语义相同
        var map = new Dictionary<string, Brother>();
        foreach (var b in brothers)
        {
            if (b.Fingerprint != null) map[b.Fingerprint] = b;
        }
        return map;
    }

    private static Dictionary<string, int[]> DeepCopy(Dictionary<string, int[]> src)
    {
        var copy = new Dictionary<string, int[]>();
        foreach (var (k, v) in src) copy[k] = (int[])v.Clone();
        return copy;
    }

    private static bool IsNullOrZero(int[]? arr)
    {
        if (arr == null) return true;
        foreach (var v in arr)
            if (v != 0) return false;
        return true;
    }

    private static int[] NullToZero(int[]? arr) => arr ?? new int[Stat.Values.Length];

    private static string ArrayToString(int[] arr) => "[" + string.Join(", ", arr) + "]"; // Arrays.toString

    private static List<string> ListSavSnapshots(string? dir)
    {
        if (dir == null || !Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateFiles(dir)
                    .Where(p => Path.GetFileName(p).EndsWith(".sav", StringComparison.Ordinal))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
