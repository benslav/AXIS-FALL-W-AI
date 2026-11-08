using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the game for Windows and Linux, as a normal desktop game or a headless dedicated server.
/// Everything goes to Builds/ (ignored by git). Cross-building Linux from Windows works because
/// the project uses the Mono scripting backend (no Linux C++ toolchain needed).
///
/// From the Editor: Tools > Gravity Portals > Build > ...
/// From a terminal (close the Editor first, or the project is locked):
///   Unity -batchmode -quit -projectPath &lt;project&gt; -executeMethod GravityPortalsBuild.BuildAll -logFile -
///   (also: BuildWindows, BuildLinux, BuildLinuxServer, BuildWindowsServer)
/// </summary>
public static class GravityPortalsBuild
{
    const string GameName = "AXIS-FALL";
    const string OutputRoot = "Builds";
    static readonly string[] MapScenes =
    {
        "Assets/GravityPortals/Scenes/GravityArena.unity",
        "Assets/GravityPortals/Scenes/TriGravityAtrium.unity",
    };

    [MenuItem("Tools/Gravity Portals/Build/Windows (game)")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, server: false, "Windows", $"{GameName}.exe");

    [MenuItem("Tools/Gravity Portals/Build/Linux (game)")]
    public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, server: false, "Linux", $"{GameName}.x86_64");

    [MenuItem("Tools/Gravity Portals/Build/Linux (dedicated server)")]
    public static void BuildLinuxServer() => Build(BuildTarget.StandaloneLinux64, server: true, "LinuxServer", $"{GameName}-Server.x86_64");

    [MenuItem("Tools/Gravity Portals/Build/Windows (dedicated server)")]
    public static void BuildWindowsServer() => Build(BuildTarget.StandaloneWindows64, server: true, "WindowsServer", $"{GameName}-Server.exe");

    [MenuItem("Tools/Gravity Portals/Build/All")]
    public static void BuildAll()
    {
        // Run all three even if one fails (non-short-circuit &), then exit once.
        bool ok = Run(BuildTarget.StandaloneWindows64, false, "Windows", $"{GameName}.exe", out _)
                & Run(BuildTarget.StandaloneLinux64, false, "Linux", $"{GameName}.x86_64", out _)
                & Run(BuildTarget.StandaloneLinux64, true, "LinuxServer", $"{GameName}-Server.x86_64", out _);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else if (ok) EditorUtility.RevealInFinder(OutputRoot);
    }

    static void Build(BuildTarget target, bool server, string folder, string executable)
    {
        bool ok = Run(target, server, folder, executable, out string path);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else if (ok) EditorUtility.RevealInFinder(path);
    }

    static bool Run(BuildTarget target, bool server, string folder, string executable, out string path)
    {
        path = Path.Combine(OutputRoot, folder, executable);
        string label = $"{target}{(server ? " server" : "")}";

        if (!ModuleInstalled(target, server))
        {
            Debug.LogError($"[Build] {label}: the Unity module isn't installed. In Unity Hub: Installs > 6000.6.8f1 > Add modules > " +
                           (target == BuildTarget.StandaloneLinux64
                               ? (server ? "Linux Dedicated Server Build Support" : "Linux Build Support (Mono)")
                               : (server ? "Windows Dedicated Server Build Support" : "Windows Build Support (Mono)")));
            return false;
        }

        var scenes = MapScenes.Where(File.Exists).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[Build] No map scenes found. Build them first from Tools > Gravity Portals.");
            return false;
        }

        ApplyPlayerSettings();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
            options = BuildOptions.None,
        };

        Debug.Log($"[Build] {label} -> {path}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Build] {label} failed: {summary.result}, {summary.totalErrors} error(s).");
            return false;
        }

        if (target == BuildTarget.StandaloneLinux64) WriteLinuxLaunchers(Path.GetDirectoryName(path), executable, server);
        Debug.Log($"[Build] {label} done: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:0}s.");
        return true;
    }

    static bool ModuleInstalled(BuildTarget target, bool server)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target)) return false;
        if (!server) return true;
        string engine = BuildPipeline.GetPlaybackEngineDirectory(target, BuildOptions.None);
        string variations = Path.Combine(engine, "Variations");
        return Directory.Exists(variations) && Directory.GetDirectories(variations, "*server*").Length > 0;
    }

    /// <summary>Settings both platforms need for networked play.</summary>
    static void ApplyPlayerSettings()
    {
        // Keep simulating when the window isn't focused (a host alt-tabbing must not freeze the match).
        PlayerSettings.runInBackground = true;
        PlayerSettings.visibleInBackground = true;
        // Windowed by default so several copies can run side by side; Alt+Enter toggles fullscreen.
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.allowFullscreenSwitch = true;
        // Mono lets Windows build for Linux without a cross-compiling toolchain.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
    }

    /// <summary>Linux needs the executable bit, which a build made on Windows can't set.
    /// These scripts set it and pass sensible defaults.</summary>
    static void WriteLinuxLaunchers(string dir, string executable, bool server)
    {
        string script = server
            ? "#!/bin/sh\n" +
              "# Dedicated server. Usage: ./run-server.sh [-map GravityArena|TriGravityAtrium] [-port 7777]\n" +
              "cd \"$(dirname \"$0\")\"\n" +
              $"chmod +x \"./{executable}\"\n" +
              $"exec \"./{executable}\" -batchmode -nographics -server -logFile - \"$@\"\n"
            : "#!/bin/sh\n" +
              "# Game. Extra options: -host | -client -ip <address> | -map <name> | -port <n>\n" +
              "cd \"$(dirname \"$0\")\"\n" +
              $"chmod +x \"./{executable}\"\n" +
              $"exec \"./{executable}\" \"$@\"\n";
        string name = server ? "run-server.sh" : "run.sh";
        File.WriteAllText(Path.Combine(dir, name), script.Replace("\r\n", "\n")); // Linux needs LF line endings
    }
}
