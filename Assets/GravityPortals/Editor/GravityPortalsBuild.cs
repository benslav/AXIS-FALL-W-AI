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
        "Assets/GravityPortals/Scenes/MainMenu.unity", // first: the game starts on the menu
        "Assets/GravityPortals/Scenes/TriGravityAtrium.unity",
        "Assets/GravityPortals/Scenes/TriGravityAtriumLarge.unity",
        "Assets/GravityPortals/Scenes/EndlessPavilion.unity",
        "Assets/GravityPortals/Scenes/ShiftingPavilion.unity",
        "Assets/GravityPortals/Scenes/HollowFortress.unity",
        "Assets/GravityPortals/Scenes/Skybridge.unity",
        "Assets/GravityPortals/Scenes/PortalTestLab.unity",
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

    // ------------------------------------------------------------------ one-click release

    const string PublishFolderKey = "ClaudeTest.GravityPortals.PublishFolder";
    static readonly string ReleaseFolder = Path.Combine(OutputRoot, "Release");

    /// <summary>Where finished zips are copied, so a shared link to that folder always has the
    /// latest build. Defaults to "AXIS-FALL Builds" in Google Drive for desktop (found on
    /// whichever drive letter it uses), then OneDrive, then Builds/Publish.
    /// Override with Tools > Gravity Portals > Build > Choose Publish Folder.</summary>
    static string PublishFolder
    {
        get
        {
            string saved = EditorPrefs.GetString(PublishFolderKey, "");
            if (!string.IsNullOrEmpty(saved)) return saved;
            string cloud = GoogleDriveFolder() ?? Environment.GetEnvironmentVariable("OneDrive");
            return string.IsNullOrEmpty(cloud)
                ? Path.GetFullPath(Path.Combine(OutputRoot, "Publish"))
                : Path.Combine(cloud, $"{GameName} Builds");
        }
        set => EditorPrefs.SetString(PublishFolderKey, value);
    }

    /// <summary>Google Drive for desktop's "My Drive" folder, or null if it isn't installed.</summary>
    static string GoogleDriveFolder()
    {
        // Streaming mode: a virtual drive (usually G:) with "My Drive" at its root.
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                string myDrive = Path.Combine(drive.RootDirectory.FullName, "My Drive");
                if (Directory.Exists(myDrive)) return myDrive;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // Mirror mode: a normal folder in your user profile.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[] { "My Drive", Path.Combine("Google Drive", "My Drive"), "Google Drive" })
        {
            string path = Path.Combine(home, candidate);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Bumps the version, builds Windows and Linux, zips each, and copies the zips plus a
    /// RELEASE.txt to the publish folder (same file names every time, so shared links stay
    /// valid), then pushes both builds to itch.io. Players on an older build are told to update
    /// when they try to join.
    /// </summary>
    [MenuItem("Tools/Gravity Portals/Release (build Windows + Linux, publish to Drive + itch.io)", priority = 0)]
    public static void Release()
    {
        string previous = PlayerSettings.bundleVersion;
        string version = NextVersion(previous);
        PlayerSettings.bundleVersion = version;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Release] v{previous} -> v{version}");

        bool ok = false;
        try
        {
            ok = BuildAndZip(BuildTarget.StandaloneWindows64, "Windows", $"{GameName}.exe", version)
              && BuildAndZip(BuildTarget.StandaloneLinux64, "Linux", $"{GameName}.x86_64", version)
              && Publish(version);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Release] Failed: {e}");
        }

        // The builds are already out on Drive; if the itch.io upload fails it can be retried
        // on its own (Tools > Gravity Portals > Build > Push Last Release to itch.io).
        if (ok) PushToItch(version);

        if (!ok)
        {
            // Don't burn a version number on a release that didn't happen.
            PlayerSettings.bundleVersion = previous;
            AssetDatabase.SaveAssets();
            Debug.LogError($"[Release] Release failed; version kept at v{previous}. See the errors above.");
        }
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    [MenuItem("Tools/Gravity Portals/Build/Choose Publish Folder...")]
    static void ChoosePublishFolder()
    {
        string chosen = EditorUtility.OpenFolderPanel("Folder to publish release zips to (e.g. a shared OneDrive or Google Drive folder)", PublishFolder, "");
        if (string.IsNullOrEmpty(chosen)) return;
        PublishFolder = chosen;
        Debug.Log($"[Release] Publish folder: {chosen}");
    }

    [MenuItem("Tools/Gravity Portals/Build/Open Publish Folder")]
    static void OpenPublishFolder()
    {
        Directory.CreateDirectory(PublishFolder);
        EditorUtility.RevealInFinder(PublishFolder);
    }

    static string NextVersion(string current)
    {
        // 0.1.7 -> 0.1.8; anything unparsable restarts at 0.1.1.
        var parts = (current ?? "").Split('.');
        if (parts.Length == 3 && int.TryParse(parts[2], out int patch))
            return $"{parts[0]}.{parts[1]}.{patch + 1}";
        return "0.1.1";
    }

    static bool BuildAndZip(BuildTarget target, string folder, string executable, string version)
    {
        string dir = Path.Combine(OutputRoot, folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, true); // no stale files from older builds
        if (!Run(target, false, folder, executable, out _)) return false;

        Directory.CreateDirectory(ReleaseFolder);
        string zip = Path.Combine(ReleaseFolder, $"{GameName}-{folder}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            string root = Path.GetFullPath(dir);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length + 1).Replace('\\', '/');
                // Unity's debug-symbol folders are explicitly not meant to ship.
                if (relative.Contains("DoNotShip") || relative.Contains("DontShip")) continue;
                System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, file, $"{GameName}/{relative}");
            }
            var info = archive.CreateEntry($"{GameName}/VERSION.txt");
            using (var w = new StreamWriter(info.Open())) w.Write($"{GameName} v{version}\n");
        }
        Debug.Log($"[Release] Zipped {folder}: {new FileInfo(zip).Length / (1024 * 1024)} MB");
        return true;
    }

    static bool Publish(string version)
    {
        string target = PublishFolder;
        Directory.CreateDirectory(target);
        foreach (string zip in Directory.GetFiles(ReleaseFolder, "*.zip"))
            File.Copy(zip, Path.Combine(target, Path.GetFileName(zip)), true);

        string notes =
            $"{GameName} v{version}  ({DateTime.Now:yyyy-MM-dd HH:mm})\n\n" +
            "Windows: unzip, then run AXIS-FALL/AXIS-FALL.exe\n" +
            "Linux:   unzip, then run:  sh AXIS-FALL/run.sh\n\n" +
            "Everyone in a match needs the same version; the game tells you if yours is out of date.\n\n" +
            "Recent changes:\n" + RecentChanges() + "\n";
        File.WriteAllText(Path.Combine(target, "RELEASE.txt"), notes);

        Debug.Log($"[Release] v{version} published to {target}");
        if (!Application.isBatchMode) EditorUtility.RevealInFinder(Path.Combine(target, "RELEASE.txt"));
        return true;
    }

    // ------------------------------------------------------------------ itch.io

    /// <summary>The itch.io page the builds go to (user/game). Players install the game with the
    /// itch app, which then downloads only what changed in each release, like Steam.</summary>
    const string ItchTarget = "beardedgoblin/axisfall";

    static readonly (string folder, string channel, string executable)[] ItchChannels =
    {
        ("Windows", "windows", $"{GameName}.exe"),
        ("Linux", "linux", $"{GameName}.x86_64"),
    };

    [MenuItem("Tools/Gravity Portals/Build/Push Last Release to itch.io")]
    static void PushLastReleaseToItch() => PushToItch(PlayerSettings.bundleVersion);

    /// <summary>Uploads the Windows and Linux builds with butler (itch.io's uploader). Butler
    /// only sends what changed since the last push, and the itch app updates players the same way.</summary>
    static bool PushToItch(string version)
    {
        string butler = FindButler();
        if (butler == null)
        {
            Debug.LogError("[itch] butler not found. Install the itch app (it brings butler along) or put butler on your PATH.");
            return false;
        }

        bool ok = true;
        try
        {
            for (int i = 0; i < ItchChannels.Length && ok; i++)
            {
                var (folder, channel, executable) = ItchChannels[i];
                string dir = Path.GetFullPath(Path.Combine(OutputRoot, folder));
                if (!File.Exists(Path.Combine(dir, executable)))
                {
                    Debug.LogError($"[itch] No {folder} build in {dir}. Run Release first.");
                    ok = false;
                    break;
                }
                File.WriteAllText(Path.Combine(dir, "VERSION.txt"), $"{GameName} v{version}\n");
                // Tells the itch app which file starts the game (there are other .exe files in a Unity build).
                File.WriteAllText(Path.Combine(dir, ".itch.toml"),
                    $"[[actions]]\nname = \"play\"\npath = \"{executable}\"\n");

                string args = $"push \"{dir}\" {ItchTarget}:{channel} --userversion {version} " +
                              "--ignore \"*DoNotShip*\" --ignore \"*DontShip*\"";
                ok = RunButler(butler, args, $"Uploading {folder} v{version} to itch.io", (float)i / ItchChannels.Length);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (ok) Debug.Log($"[itch] v{version} pushed to https://{ItchTarget.Split('/')[0]}.itch.io/{ItchTarget.Split('/')[1]} (players get it through the itch app).");
        else Debug.LogError("[itch] Upload failed (see above). If butler says you're not logged in, run `butler login` once. " +
                            "Retry with Tools > Gravity Portals > Build > Push Last Release to itch.io.");
        return ok;
    }

    /// <summary>butler from the itch app (the version it currently uses), else from PATH.</summary>
    static string FindButler()
    {
        string broth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "broth", "butler");
        string exe = Application.platform == RuntimePlatform.WindowsEditor ? "butler.exe" : "butler";
        try
        {
            string chosen = Path.Combine(broth, ".chosen-version");
            if (File.Exists(chosen))
            {
                string path = Path.Combine(broth, "versions", File.ReadAllText(chosen).Trim(), exe);
                if (File.Exists(path)) return path;
            }
            string versions = Path.Combine(broth, "versions");
            if (Directory.Exists(versions))
            {
                string newest = Directory.GetDirectories(versions)
                    .Select(d => Path.Combine(d, exe)).Where(File.Exists)
                    .OrderByDescending(p => File.GetLastWriteTimeUtc(p)).FirstOrDefault();
                if (newest != null) return newest;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[itch] Couldn't look in the itch app's folder: {e.Message}");
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string path = Path.Combine(dir.Trim(), exe);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException) { } // malformed PATH entry
        }
        return null;
    }

    static bool RunButler(string butler, string args, string title, float progress)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(butler, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var output = new System.Text.StringBuilder();
        using (var p = new System.Diagnostics.Process { StartInfo = psi })
        {
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            var started = DateTime.Now;
            while (!p.WaitForExit(250))
            {
                if (!Application.isBatchMode)
                    EditorUtility.DisplayProgressBar("Release", $"{title}... ({(DateTime.Now - started).TotalSeconds:0}s)", progress);
                if ((DateTime.Now - started).TotalMinutes > 30)
                {
                    try { p.Kill(); } catch { }
                    Debug.LogError("[itch] butler took over 30 minutes; stopped it.");
                    return false;
                }
            }
            p.WaitForExit(); // flush the output readers
            string log;
            lock (output) log = output.ToString();
            if (p.ExitCode == 0)
            {
                Debug.Log($"[itch] {title}: done.\n{log}");
                return true;
            }
            Debug.LogError($"[itch] {title}: butler exited with code {p.ExitCode}.\n{log}");
            return false;
        }
    }

    /// <summary>The last few commit subjects, if this project is a git repository.</summary>
    static string RecentChanges()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "log -8 --pretty=format:\"- %s\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath("."),
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return string.IsNullOrWhiteSpace(output) ? "- (no history)" : output;
            }
        }
        catch
        {
            return "- (git not available)";
        }
    }

    // ------------------------------------------------------------------ single builds

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
              "# Dedicated server. Usage: ./run-server.sh [-map TriGravityAtrium|EndlessPavilion] [-port 7777]\n" +
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
