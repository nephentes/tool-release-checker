namespace ToolReleaseChecker;

internal enum AssemblyKind
{
    Managed,
    Native,
    Error,
}

/// <summary>Overall build verdict, in increasing order of concern.</summary>
internal enum BuildVerdict
{
    Release,
    Unoptimized,
    Debug,
}

/// <summary>Result of inspecting a single DLL or EXE.</summary>
/// <param name="Display">File name (or relative path) shown in the report.</param>
/// <param name="Kind">Whether the file is a managed assembly, native, or failed to load.</param>
/// <param name="Optimized">JIT optimizer enabled (Release hallmark). Only meaningful when <see cref="Kind"/> is Managed.</param>
/// <param name="HasDebugInfo">JIT tracking / debuggable codegen enabled. Common in Release too, so informational only.</param>
/// <param name="HasDebugCode">DEBUG-conditional code appears to have survived compilation (heuristic).</param>
/// <param name="Framework">Friendly target framework from TargetFrameworkAttribute (e.g. ".NET 8.0"), or "unknown".</param>
/// <param name="Detail">Free-form note about how the verdict was reached.</param>
internal sealed record BuildInfo(
    string Display,
    AssemblyKind Kind,
    bool Optimized,
    bool HasDebugInfo,
    bool HasDebugCode,
    string Framework,
    string Detail)
{
    /// <summary>
    /// True if the assembly shows a hallmark of a Debug build that shouldn't ship:
    /// the JIT optimizer is disabled, or DEBUG-conditional code survived.
    /// Debug info (JIT tracking) alone is NOT counted — it's common in Release too.
    /// Used to drive the <c>--debug-only</c> filter and the release-gate exit code.
    /// </summary>
    public bool LooksLikeDebug => Kind == AssemblyKind.Managed && Verdict != BuildVerdict.Release;

    /// <summary>
    /// Overall verdict for a managed assembly:
    /// <list type="bullet">
    /// <item><b>Debug</b> — DEBUG-conditional code survived, or the optimizer is off
    /// while JIT tracking is on (the hallmark of the Debug configuration).</item>
    /// <item><b>Unoptimized</b> — optimizer off but without the other Debug markers.</item>
    /// <item><b>Release</b> — optimized, no DEBUG code.</item>
    /// </list>
    /// </summary>
    public BuildVerdict Verdict =>
        HasDebugCode || (!Optimized && HasDebugInfo) ? BuildVerdict.Debug :
        !Optimized ? BuildVerdict.Unoptimized :
        BuildVerdict.Release;

    /// <summary>
    /// Human-readable roll-up shown in the report's last column. For managed
    /// assemblies it explains the verdict; for native/error rows it carries the
    /// underlying note (e.g. "not a .NET assembly" or the exception message).
    /// </summary>
    public string Summary => Kind switch
    {
        AssemblyKind.Managed => Verdict switch
        {
            BuildVerdict.Debug when HasDebugCode && !Optimized => "Debug build (unoptimized + DEBUG code)",
            BuildVerdict.Debug when HasDebugCode => "Debug build (optimized, but DEBUG code present)",
            BuildVerdict.Debug => "Debug build (unoptimized + debug info)",
            BuildVerdict.Unoptimized => "unoptimized",
            _ => "Release",
        },
        _ => Detail,
    };
}
