using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Command-line build entry points: one for the Windows player, one for the
// Linux dedicated server, invoked as
//   Unity.exe -batchmode -quit -executeMethod BuildCommands.<Method>
// so a build can be kicked off without clicking through the Build Settings
// window. Both are also on the Encounter/Build menu for use from the open
// Editor. Editor-only via this folder's own Encounter.Editor.asmdef
// (includePlatforms: Editor) - see IconWiringTool.cs for why the folder
// name alone isn't enough. Tools/CompileCheck.csproj still compiles it (it
// defines UNITY_EDITOR and globs Assets/Scripts/**).
public static class BuildCommands
{
    private const string WindowsClientPath = "Builds/WindowsClient/encounter.exe";

    // The VPS launch command depends on this exact filename - don't rename.
    private const string LinuxServerPath = "Builds/LinuxServer/linuxserverbuild_reallyfungame.x86_64";

    [MenuItem("Encounter/Build/Windows Client")]
    public static void BuildWindowsClient()
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        Run(new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = WindowsClientPath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None,
        });
    }

    [MenuItem("Encounter/Build/Linux Server")]
    public static void BuildLinuxServer()
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
        Run(new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = LinuxServerPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        });
    }

    private static string[] EnabledScenes()
    {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
    }

    // Runs the build and reports one summary line. In batch mode the process
    // exit code is the only thing the command line can see, so a failed
    // build exits 1 and a successful one exits 0; from the menu we only log,
    // since EditorApplication.Exit would close the user's Editor.
    private static void Run(BuildPlayerOptions options)
    {
        string directory = Path.GetDirectoryName(options.locationPathName);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        string info = $"[BuildCommands] {summary.result}: {summary.outputPath} " +
                      $"({summary.totalSize / (1024f * 1024f):F1} MB, " +
                      $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s), " +
                      $"{summary.totalTime.TotalSeconds:F0}s)";

        bool succeeded = summary.result == BuildResult.Succeeded;
        if (succeeded) Debug.Log(info);
        else Debug.LogError(info);

        if (Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
    }
}
