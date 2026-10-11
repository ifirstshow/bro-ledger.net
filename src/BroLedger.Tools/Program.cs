namespace BroLedger.Tools;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "savefile" => Parser.BrotherSavefileExporter.Run(args[1..]),
            "replay"   => ReplayAnalyzer.Run(args[1..]),
            "csv"      => BrotherCsvExporter.Run(args[1..]),
            _          => UnknownVerb(args[0]),
        };
    }

    private static int NotYetAvailable(string verb)
    {
        Console.Error.WriteLine($"{verb}: not yet available");
        return 1;
    }

    private static int UnknownVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown verb: {verb}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: bro-ledger-tools <verb> [args...]");
        Console.Error.WriteLine("Verbs:");
        Console.Error.WriteLine("  savefile <save.sav> <output.tsv>  Write per-brother TSV parse trace");
        Console.Error.WriteLine("  replay                            Replay save-history analysis");
        Console.Error.WriteLine("  csv                               Write brothers CSV");
    }
}
