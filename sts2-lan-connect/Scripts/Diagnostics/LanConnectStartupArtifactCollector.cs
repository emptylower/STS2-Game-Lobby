using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectStartupArtifactCollector
{
    public static void Capture(LanConnectStartupDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Assembly modAssembly = typeof(Entry).Assembly;
        Assembly gameAssembly = typeof(ModInitializerAttribute).Assembly;
        Assembly harmonyAssembly = typeof(Harmony).Assembly;
        Assembly? loadedMonoModAssembly = FindLoadedAssembly("MonoMod.Utils");
        Assembly monoModContainer = loadedMonoModAssembly ?? harmonyAssembly;
        string? monoModVersion = loadedMonoModAssembly == null
            ? harmonyAssembly.GetReferencedAssemblies()
                .FirstOrDefault(static reference =>
                    string.Equals(reference.Name, "MonoMod.Utils", StringComparison.OrdinalIgnoreCase))?
                .Version?
                .ToString()
            : null;
        Assembly? mobileAssembly = FindLoadedAssembly("STS2Mobile");
        Assembly? ritsuAssembly = FindLoadedAssembly("RitsuLib");

        CaptureComponent(diagnostics, "game", gameAssembly, embedded: false);
        CaptureComponent(diagnostics, "mod", modAssembly, embedded: false);
        CaptureComponent(diagnostics, "harmony", harmonyAssembly, embedded: false);
        CaptureComponent(
            diagnostics,
            "monomod",
            monoModContainer,
            embedded: loadedMonoModAssembly == null,
            versionOverride: monoModVersion);
        CaptureComponent(diagnostics, "sts2mobile", mobileAssembly, embedded: false);
        CaptureComponent(diagnostics, "ritsulib", ritsuAssembly, embedded: false);

        HashAssembly(diagnostics, "sts2.dll", gameAssembly);
        HashAssembly(diagnostics, "sts2_lan_connect.dll", modAssembly);
        HashAssembly(diagnostics, "0Harmony.dll", harmonyAssembly);
        HashAssembly(diagnostics, "STS2Mobile.dll", mobileAssembly);
        HashAssembly(diagnostics, "RitsuLib.dll", ritsuAssembly);

        CapturePackagedArtifacts(diagnostics);
    }

    private static void CaptureComponent(
        LanConnectStartupDiagnostics diagnostics,
        string component,
        Assembly? assembly,
        bool embedded,
        string? versionOverride = null)
    {
        if (assembly == null)
        {
            diagnostics.RecordInfo(
                "component_info",
                new Dictionary<string, object?>
                {
                    ["component"] = component,
                    ["state"] = "not_loaded"
                });
            return;
        }

        string? informationalVersion = null;
        string? moduleMvid = null;
        try
        {
            informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
        }
        catch
        {
        }
        try
        {
            moduleMvid = assembly.ManifestModule.ModuleVersionId.ToString("D");
        }
        catch
        {
        }

        diagnostics.RecordInfo(
            "component_info",
            new Dictionary<string, object?>
            {
                ["component"] = component,
                ["state"] = "loaded",
                ["assembly"] = assembly.GetName().Name,
                ["version"] = versionOverride ?? assembly.GetName().Version?.ToString(),
                ["informational_version"] = informationalVersion,
                ["module_mvid"] = moduleMvid,
                ["embedded"] = embedded
            });
    }

    private static void HashAssembly(
        LanConnectStartupDiagnostics diagnostics,
        string artifact,
        Assembly? assembly)
    {
        if (assembly == null)
        {
            return;
        }

        try
        {
            HashFile(diagnostics, artifact, assembly.Location);
        }
        catch (Exception exception)
        {
            diagnostics.Warn("artifact_location", exception);
        }
    }

    private static void CapturePackagedArtifacts(LanConnectStartupDiagnostics diagnostics)
    {
        try
        {
            string modDirectory = LanConnectPaths.ResolveModDirectory();
            HashFirstExisting(
                diagnostics,
                "manifest",
                Path.Combine(modDirectory, "sts2_lan_connect.json"),
                Path.Combine(modDirectory, "mod_manifest.json"));
            HashFirstExisting(
                diagnostics,
                "pck",
                Path.Combine(modDirectory, "sts2_lan_connect.pck"));
        }
        catch (Exception exception)
        {
            diagnostics.Warn("packaged_artifact_discovery", exception);
        }
    }

    private static void HashFirstExisting(
        LanConnectStartupDiagnostics diagnostics,
        string artifact,
        params string[] candidates)
    {
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path != null)
        {
            HashFile(diagnostics, artifact, path);
        }
    }

    private static void HashFile(
        LanConnectStartupDiagnostics diagnostics,
        string artifact,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            string digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            diagnostics.RecordInfo(
                "artifact_hash",
                new Dictionary<string, object?>
                {
                    ["artifact"] = artifact,
                    ["file_name"] = Path.GetFileName(path),
                    ["sha256"] = digest,
                    ["size_bytes"] = stream.Length
                });
        }
        catch (Exception exception)
        {
            diagnostics.Warn($"artifact_hash_{artifact}", exception);
        }
    }

    private static Assembly? FindLoadedAssembly(string expectedName)
    {
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
        {
            string? name = assembly.GetName().Name;
            return string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase) ||
                   (name?.Contains(expectedName, StringComparison.OrdinalIgnoreCase) ?? false);
        });
    }
}
