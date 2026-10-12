namespace ToolReleaseChecker;

/// <summary>Renders the inspection results as an aligned, colored table.</summary>
internal static class Report
{
    private const int FlagWidth = 9; // fits "OPTIMIZED"

    public static void Print(IReadOnlyList<BuildInfo> rows)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine("(nothing to show)");
            return;
        }

        int nameWidth = Math.Max("FILE".Length, rows.Max(r => r.Display.Length));
        int fwWidth = Math.Max("FRAMEWORK".Length, rows.Max(r => DisplayFramework(r).Length));

        WriteHeader(nameWidth, fwWidth);
        foreach (var r in rows)
            WriteRow(r, nameWidth, fwWidth);
    }

    private static void WriteHeader(int nameWidth, int fwWidth)
    {
        Console.WriteLine(
            $"{"FILE".PadRight(nameWidth)}  {"FRAMEWORK".PadRight(fwWidth)}  {"OPTIMIZED".PadRight(FlagWidth)}  " +
            $"{"DBG INFO".PadRight(FlagWidth)}  {"DBG CODE".PadRight(FlagWidth)}  SUMMARY");
        Console.WriteLine(
            $"{new string('-', nameWidth)}  {new string('-', fwWidth)}  {new string('-', FlagWidth)}  " +
            $"{new string('-', FlagWidth)}  {new string('-', FlagWidth)}  -------");
    }

    private static void WriteRow(BuildInfo r, int nameWidth, int fwWidth)
    {
        Console.Write(r.Display.PadRight(nameWidth));
        Console.Write("  ");
        WriteCell(DisplayFramework(r), ConsoleColor.Cyan, fwWidth);
        Console.Write("  ");

        if (r.Kind != AssemblyKind.Managed)
        {
            ConsoleColor color = r.Kind == AssemblyKind.Native ? ConsoleColor.DarkGray : ConsoleColor.Red;
            string label = r.Kind == AssemblyKind.Native ? "native" : "error";
            WriteCell(label, color);
            Console.Write("  ");
            WriteCell("-", ConsoleColor.DarkGray);
            Console.Write("  ");
            WriteCell("-", ConsoleColor.DarkGray);
            Console.Write("  ");
            WriteLineColored(r.Summary, color);
            return;
        }

        // OPTIMIZED: enabled is good (green), disabled is a Debug hallmark (yellow).
        WriteCell(YesNo(r.Optimized), r.Optimized ? ConsoleColor.Green : ConsoleColor.Yellow);
        Console.Write("  ");
        // DBG INFO: informational only (Release often ships PDBs too) — neutral.
        WriteCell(YesNo(r.HasDebugInfo), ConsoleColor.DarkGray);
        Console.Write("  ");
        // DBG CODE: surviving DEBUG code is a concern (yellow); absent is good (green).
        WriteCell(YesNo(r.HasDebugCode), r.HasDebugCode ? ConsoleColor.Yellow : ConsoleColor.Green);
        Console.Write("  ");
        // SUMMARY: red for Debug, yellow for unoptimized, green for a clean Release.
        WriteLineColored(r.Summary, SummaryColor(r));
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    // Native/error rows have no framework; show a dash.
    private static string DisplayFramework(BuildInfo r) =>
        r.Kind == AssemblyKind.Managed ? r.Framework : "-";

    private static ConsoleColor SummaryColor(BuildInfo r) => r.Verdict switch
    {
        BuildVerdict.Debug => ConsoleColor.Red,
        BuildVerdict.Unoptimized => ConsoleColor.Yellow,
        _ => ConsoleColor.Green,
    };

    private static void WriteCell(string text, ConsoleColor color, int width = FlagWidth)
    {
        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text.PadRight(width));
        Console.ForegroundColor = prev;
    }

    private static void WriteLineColored(string text, ConsoleColor color)
    {
        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }
}
