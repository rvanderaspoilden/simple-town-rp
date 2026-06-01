using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Entry point invoked by GitHub Actions via
///   Unity -quit -batchmode -executeMethod CiBuild.BuildServer
/// (the game-ci/unity-builder action handles the Unity invocation; we just
/// expose the static method it calls).
///
/// Mirrors the manual "Server Linux" Build Profile choices in one place so
/// the CI doesn't depend on a Build Profile asset (whose serialized format
/// is more fragile than direct PlayerSettings calls). Specifically:
///   - Linux x86_64
///   - IL2CPP scripting backend
///   - Standalone subtarget = Server (strips the client renderer)
///   - STRESS_TEST_BOTS removed from the Server build's define list
///   - Scenes pulled from EditorBuildSettings (single source of truth)
///
/// Lives under Assets/Editor/ so it is excluded from runtime builds and
/// compiled into the Editor-only assembly.
/// </summary>
public static class CiBuild {

    private const string OUTPUT_PATH = "build/Server/Simple Town.x86_64";

    public static void BuildServer() {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0) {
            Debug.LogError("[CiBuild] No enabled scenes in EditorBuildSettings — aborting.");
            EditorApplication.Exit(2);
            return;
        }

        // Server subtarget strips the client renderer and audio output. Combined
        // with IL2CPP it produces a Linux dedicated server binary identical to
        // what the "Server Linux" Build Profile produces in the Editor UI.
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);

        var serverTarget = NamedBuildTarget.Server;
        PlayerSettings.SetScriptingBackend(serverTarget, ScriptingImplementation.IL2CPP);

        // STRESS_TEST_BOTS must never appear in a Server build. It's set on the
        // Bot Headless profile only, but on a fresh CI runner the project-level
        // defines may inherit it — strip it explicitly to be safe.
        PlayerSettings.GetScriptingDefineSymbols(serverTarget, out string[] currentDefines);
        var serverDefines = (currentDefines ?? new string[0])
            .Where(d => d != "STRESS_TEST_BOTS")
            .ToArray();
        PlayerSettings.SetScriptingDefineSymbols(serverTarget, serverDefines);

        Directory.CreateDirectory(Path.GetDirectoryName(OUTPUT_PATH) ?? ".");

        var opts = new BuildPlayerOptions {
            scenes = scenes,
            locationPathName = OUTPUT_PATH,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        Debug.Log($"[CiBuild] Building Linux64 Server (IL2CPP) → {OUTPUT_PATH}");
        Debug.Log($"[CiBuild] Scenes ({scenes.Length}): {string.Join(", ", scenes)}");
        Debug.Log($"[CiBuild] Server defines: {string.Join(";", serverDefines)}");

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        Debug.Log($"[CiBuild] Result={summary.result}  Size={summary.totalSize}  Duration={summary.totalTime}  Errors={summary.totalErrors}");

        if (summary.result != BuildResult.Succeeded) {
            // Non-zero exit so the GitHub Actions step fails properly. Without
            // this, BuildPlayer logs errors but Unity still exits 0.
            EditorApplication.Exit(1);
        }
    }
}
