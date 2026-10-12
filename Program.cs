using ToolReleaseChecker;

// tool-release-checker (relcheck)
// Lists DLLs and EXEs in a directory and reports, for each managed assembly, whether it
// is optimized, carries debug info, and whether DEBUG-conditional code survived.

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintUsage();
    return 0;
}

string directory = options.Directory ?? Directory.GetCurrentDirectory();
if (!Directory.Exists(directory))
{
    Console.Error.WriteLine($"Directory not found: {directory}");
    return 2;
}

var searchOption = options.Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
string[] files;
try
{
    files = Directory.GetFiles(directory, "*.dll", searchOption)
        .Concat(Directory.GetFiles(directory, "*.exe", searchOption))
        .ToArray();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to enumerate '{directory}': {ex.Message}");
    return 2;
}

Array.Sort(files, StringComparer.OrdinalIgnoreCase);

if (files.Length == 0)
{
    Console.WriteLine($"No .dll or .exe files found in {directory}" + (options.Recurse ? " (recursive)." : "."));
    return 0;
}

Console.WriteLine($"Scanning {files.Length} file(s) in {directory}" + (options.Recurse ? " (recursive)" : "") + Environment.NewLine);

var inspector = new AssemblyInspector();
var results = new List<BuildInfo>(files.Length);
foreach (var file in files)
{
    string display = options.Recurse ? Path.GetRelativePath(directory, file) : Path.GetFileName(file);
    results.Add(inspector.Inspect(file, display));
}

// Optional filtering of the displayed rows.
IEnumerable<BuildInfo> rows = results;
if (options.OnlyDebug)
    rows = rows.Where(r => r.LooksLikeDebug);
else if (options.OnlyManaged)
    rows = rows.Where(r => r.Kind == AssemblyKind.Managed);

Report.Print(rows.ToList());

int managed = results.Count(r => r.Kind == AssemblyKind.Managed);
Console.WriteLine();
Console.WriteLine($"Total: {results.Count}   "
    + $"Managed: {managed}   "
    + $"Debug: {results.Count(r => r.Kind == AssemblyKind.Managed && r.Verdict == BuildVerdict.Debug)}   "
    + $"Unoptimized: {results.Count(r => r.Kind == AssemblyKind.Managed && r.Verdict == BuildVerdict.Unoptimized)}   "
    + $"Release: {results.Count(r => r.Kind == AssemblyKind.Managed && r.Verdict == BuildVerdict.Release)}   "
    + $"Native/Other: {results.Count(r => r.Kind is AssemblyKind.Native or AssemblyKind.Error)}");

// Exit code 1 if any assembly looks like a Debug build (Debug or unoptimized
// verdict), so this can gate a release pipeline.
return results.Any(r => r.LooksLikeDebug) ? 1 : 0;
