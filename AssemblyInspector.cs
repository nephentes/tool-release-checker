using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ToolReleaseChecker;

/// <summary>
/// Inspects DLLs/EXEs and reports three independent build characteristics, reading
/// metadata only (assemblies are never executed):
/// <list type="bullet">
/// <item><b>Optimized</b> — from <see cref="DebuggableAttribute"/>'s optimizer bit.</item>
/// <item><b>Debug info</b> — from <see cref="DebuggableAttribute"/>'s JIT-tracking bit.</item>
/// <item><b>DEBUG code</b> — heuristic: whether DEBUG-conditional calls survived.</item>
/// </list>
/// </summary>
internal sealed class AssemblyInspector
{
    // DebuggableAttribute.DebuggingModes flag values.
    private const int EnableJitTracking = 0x1;     // DebuggingModes.Default — debuggable codegen / sequence points.
    private const int DisableOptimizations = 0x100; // DebuggingModes.DisableOptimizations.

    private readonly string[] _runtimeAssemblies =
        Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");

    public BuildInfo Inspect(string path, string display)
    {
        // A fresh context per file keeps things simple and isolates failures.
        var resolver = new PathAssemblyResolver(_runtimeAssemblies.Append(path));
        try
        {
            using var mlc = new MetadataLoadContext(resolver);
            Assembly asm = mlc.LoadFromAssemblyPath(path);

            CustomAttributeData? debuggable = FindAttribute(asm, typeof(DebuggableAttribute));

            (bool optimized, bool debugInfo) = ReadDebuggable(debuggable);
            bool debugCode = HasSurvivingDebugCode(path);
            string framework = ReadTargetFramework(asm);

            // Detail is unused for managed rows — the report derives a verdict via
            // BuildInfo.Summary. It only carries a message for native/error rows.
            return new BuildInfo(display, AssemblyKind.Managed, optimized, debugInfo, debugCode, framework, "");
        }
        catch (BadImageFormatException)
        {
            // Native DLL/EXE (incl. .NET Core apphost launchers) or otherwise not a managed assembly.
            return new BuildInfo(display, AssemblyKind.Native, false, false, false, "", "not a .NET assembly");
        }
        catch (Exception ex)
        {
            return new BuildInfo(display, AssemblyKind.Error, false, false, false, "", ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Reads the <see cref="DebuggableAttribute"/> constructor arguments to determine
    /// whether the JIT optimizer was enabled and whether JIT tracking (debug info) is on.
    /// </summary>
    private static (bool Optimized, bool DebugInfo) ReadDebuggable(CustomAttributeData? data)
    {
        // No attribute => optimizations enabled, no JIT tracking => a clean Release build.
        if (data is null)
            return (true, false);

        var args = data.ConstructorArguments;

        // Modern compilers: DebuggableAttribute(DebuggingModes modes)
        if (args.Count == 1 && args[0].Value is int modes)
            return ((modes & DisableOptimizations) == 0, (modes & EnableJitTracking) != 0);

        // Legacy: DebuggableAttribute(bool isJITTrackingEnabled, bool isJITOptimizerDisabled)
        if (args.Count == 2 && args[0].Value is bool jitTracking && args[1].Value is bool optimizerDisabled)
            return (!optimizerDisabled, jitTracking);

        // Unrecognized shape: be conservative and treat as optimized / no debug info.
        return (true, false);
    }

    /// <summary>
    /// Finds an assembly-level attribute by type, tolerating references we can't
    /// resolve. Touching <see cref="CustomAttributeData.AttributeType"/> forces the
    /// loader to resolve that attribute's defining assembly; for an attribute like a
    /// WPF assembly's <c>[ThemeInfo]</c> (in PresentationFramework) that assembly
    /// isn't on our resolver path and throws. We only care about a couple of
    /// core-runtime attributes, so skip any that fail to resolve rather than aborting.
    /// </summary>
    private static CustomAttributeData? FindAttribute(Assembly asm, Type attributeType)
    {
        foreach (CustomAttributeData a in asm.GetCustomAttributesData())
        {
            try
            {
                if (a.AttributeType.FullName == attributeType.FullName)
                    return a;
            }
            catch (Exception)
            {
                // Attribute type lives in an unresolvable assembly — not one we need.
            }
        }
        return null;
    }

    /// <summary>
    /// Reads <see cref="TargetFrameworkAttribute"/> and renders it as a friendly
    /// name (e.g. ".NET 8.0", ".NET Framework 4.7.2"). Returns "unknown" when the
    /// attribute is absent (assemblies targeting &lt; .NET 4.0 don't carry it).
    /// </summary>
    private static string ReadTargetFramework(Assembly asm)
    {
        CustomAttributeData? tfm = FindAttribute(asm, typeof(TargetFrameworkAttribute));

        if (tfm is null || tfm.ConstructorArguments.Count == 0 ||
            tfm.ConstructorArguments[0].Value is not string moniker)
        {
            return "unknown";
        }

        return FormatMoniker(moniker);
    }

    /// <summary>
    /// Turns a raw TFM like ".NETCoreApp,Version=v8.0" into a friendly label.
    /// </summary>
    private static string FormatMoniker(string moniker)
    {
        // Shape: "<Identifier>,Version=v<n.n[.n]>[,Profile=...]"
        string identifier = moniker;
        string version = "";
        foreach (var part in moniker.Split(','))
        {
            if (part.StartsWith("Version=v", StringComparison.OrdinalIgnoreCase))
                version = part["Version=v".Length..];
            else if (!part.Contains('='))
                identifier = part.Trim();
        }

        switch (identifier)
        {
            case ".NETFramework":
                return $".NET Framework {version}";
            case ".NETStandard":
                return $".NET Standard {version}";
            case ".NETCoreApp":
                // v5.0+ is just ".NET N"; v1–v3.x is ".NET Core N".
                int major = ParseMajor(version);
                return major >= 5 ? $".NET {version}" : $".NET Core {version}";
            default:
                return string.IsNullOrEmpty(version) ? moniker : $"{identifier} {version}";
        }
    }

    private static int ParseMajor(string version)
    {
        int dot = version.IndexOf('.');
        string head = dot >= 0 ? version[..dot] : version;
        return int.TryParse(head, out int major) ? major : 0;
    }

    /// <summary>
    /// Heuristic check for surviving DEBUG-conditional code. Every method on
    /// <c>System.Diagnostics.Debug</c> is marked <c>[Conditional("DEBUG")]</c>, so when
    /// the DEBUG constant is undefined the compiler strips those calls and the reference
    /// to the type disappears from metadata. A surviving reference therefore indicates
    /// the assembly was compiled with DEBUG defined.
    /// <para>
    /// Limitation: this only catches the BCL <c>Debug</c> class, not user-defined
    /// <c>[Conditional("DEBUG")]</c> methods (detecting those would require walking IL).
    /// </para>
    /// </summary>
    private static bool HasSurvivingDebugCode(string path)
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata)
            return false;

        MetadataReader mr = pe.GetMetadataReader();
        foreach (var handle in mr.TypeReferences)
        {
            TypeReference tr = mr.GetTypeReference(handle);
            if (mr.GetString(tr.Namespace) == "System.Diagnostics" &&
                mr.GetString(tr.Name) == "Debug")
            {
                return true;
            }
        }
        return false;
    }
}
