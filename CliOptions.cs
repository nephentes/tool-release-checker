namespace ToolReleaseChecker;

/// <summary>Parsed command-line options.</summary>
internal sealed class CliOptions
{
    public string? Directory { get; private set; }
    public bool Recurse { get; private set; }
    public bool OnlyDebug { get; private set; }
    public bool OnlyManaged { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    o.ShowHelp = true;
                    break;
                case "-r" or "--recurse":
                    o.Recurse = true;
                    break;
                case "-d" or "--debug-only":
                    o.OnlyDebug = true;
                    break;
                case "-m" or "--managed-only":
                    o.OnlyManaged = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Unknown option: {arg}");
                        o.ShowHelp = true;
                    }
                    else
                    {
                        o.Directory = SanitizePath(arg);
                    }
                    break;
            }
        }
        return o;
    }

    /// <summary>
    /// Cleans up a directory argument. On Windows, a path quoted with a trailing
    /// backslash — e.g. "C:\dir\" — has its closing quote escaped by the shell,
    /// so the argument arrives with a stray trailing double-quote. Strip that and
    /// any surrounding quotes/whitespace so the path still resolves.
    /// </summary>
    private static string SanitizePath(string arg)
        => arg.Trim().Trim('"').Trim();

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            relcheck - report optimization / debug characteristics of .NET DLLs and EXEs.

            For each managed assembly it reports:
              FRAMEWORK  Target framework (e.g. .NET 8.0, .NET Framework 4.7.2).
              OPTIMIZED  JIT optimizer enabled (DebuggableAttribute).
              DBG INFO   JIT tracking / debuggable codegen (informational).
              DBG CODE   DEBUG-conditional code survived compilation (heuristic).

            Usage:
              relcheck [directory] [options]

            Arguments:
              directory          Folder to scan (default: current directory).

            Options:
              -r, --recurse      Scan subdirectories too.
              -d, --debug-only   Show only assemblies that look like Debug builds
                                 (unoptimized or with DEBUG code).
              -m, --managed-only Hide native / non-.NET files from the listing.
              -h, --help         Show this help.

            Exit codes:
              0  Completed, no Debug-looking assemblies found.
              1  Completed, at least one unoptimized / DEBUG-code assembly found.
              2  Invalid directory or enumeration error.
            """);
    }
}
