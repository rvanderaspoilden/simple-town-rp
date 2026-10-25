using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sim.Editor {
    /// <summary>
    /// Replicates the Starter Assets third-person animator graph (locomotion + jump/land)
    /// inside Character.controller, driven by our existing parameters:
    ///   Velocity (= SA "Speed"), Grounded, Jump, FreeFall.
    /// Locomotion: Movement = 1D blend 0 Idle / 2 Walk_N / 6 Run_N (exact SA thresholds, timeScale 1).
    /// Jump graph (exact SA transition timings, read from StarterAssetsThirdPerson.controller):
    ///   Movement  → JumpStart [Jump]      0.070
    ///   Movement  → InAir     [FreeFall]  0.038 (walking off a ledge)
    ///   JumpStart → InAir     exitTime 0.664, dur 0.470, offset 0.609
    ///   InAir     → JumpLand  [Grounded]  0.098, offset 0.080
    ///   JumpLand  → Movement  exitTime 0.399, dur 0.434, offset 0.363 (interruptible by destination)
    /// JumpLand is a 1D blend 0 JumpLand / 2 Walk_N_Land / 6 Run_N_Land.
    /// Mood locomotion variants are intentionally dropped (user call, 2026-10-25): the old
    /// mood sub-trees under Movement are destroyed. Idempotent — re-running rebuilds the same graph.
    /// </summary>
    public static class TpsStarterAssetsAnimatorSetup {
        private const string ControllerPath = "Assets/Resources/Animations/Personnage/Character.controller";
        private const string AnimDir = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/";

        [MenuItem("Tools/TPS/Setup Starter Assets Animator (Locomotion + Jump)")]
        public static void Setup() {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) {
                Debug.LogError($"[TpsStarterAssetsAnimatorSetup] Controller not found at {ControllerPath}");
                return;
            }

            AnimationClip idle      = LoadClip("Stand--Idle.anim.fbx", "Idle");
            AnimationClip walk      = LoadClip("Locomotion--Walk_N.anim.fbx", "Walk_N");
            AnimationClip run       = LoadClip("Locomotion--Run_N.anim.fbx", "Run_N");
            AnimationClip jumpStart = LoadClip("Jump--Jump.anim.fbx", "JumpStart");
            AnimationClip jumpLand  = LoadClip("Jump--Jump.anim.fbx", "JumpLand");
            AnimationClip inAirClip = LoadClip("Jump--InAir.anim.fbx", "InAir");
            AnimationClip walkLand  = LoadClip("Locomotion--Walk_N_Land.anim.fbx", "Walk_N_Land");
            AnimationClip runLand   = LoadClip("Locomotion--Run_N_Land.anim.fbx", "Run_N_Land");
            if (!idle || !walk || !run || !jumpStart || !jumpLand || !inAirClip || !walkLand || !runLand) {
                Debug.LogError("[TpsStarterAssetsAnimatorSetup] Starter Assets clips not found — is the package imported?");
                return;
            }

            EnsureBoolParameter(controller, "Grounded", true);
            EnsureBoolParameter(controller, "Jump", false);
            EnsureBoolParameter(controller, "FreeFall", false);

            AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
            AnimatorState movement = FindState(baseLayer, "Movement");
            if (movement == null) {
                Debug.LogError("[TpsStarterAssetsAnimatorSetup] 'Movement' state not found in Base Layer");
                return;
            }
            Vector3 mvPos = baseLayer.states.First(s => s.state == movement).position;

            // Locomotion: flatten the mood sub-trees into the exact SA 1D blend.
            ReplaceMotion(movement, BuildBlendTree(controller, "Movement", (idle, 0f), (walk, 2f), (run, 6f)));

            AnimatorState jump = FindState(baseLayer, "JumpStart") ?? FindState(baseLayer, "Jump")
                                 ?? baseLayer.AddState("JumpStart", mvPos + new Vector3(420f, -80f, 0f));
            jump.name = "JumpStart";
            ReplaceMotion(jump, jumpStart);

            AnimatorState inAir = FindState(baseLayer, "InAir")
                                  ?? baseLayer.AddState("InAir", mvPos + new Vector3(640f, -80f, 0f));
            ReplaceMotion(inAir, inAirClip);

            AnimatorState land = FindState(baseLayer, "JumpLand")
                                 ?? baseLayer.AddState("JumpLand", mvPos + new Vector3(640f, 80f, 0f));
            ReplaceMotion(land, BuildBlendTree(controller, "JumpLand", (jumpLand, 0f), (walkLand, 2f), (runLand, 6f)));

            // Rewire: drop previous jump-related transitions (keep Movement ↔ Action and others intact).
            foreach (var t in movement.transitions
                         .Where(t => t.destinationState == jump || t.destinationState == inAir || t.destinationState == land)
                         .ToArray())
                movement.RemoveTransition(t);
            ClearTransitions(jump);
            ClearTransitions(inAir);
            ClearTransitions(land);

            AddConditionTransition(movement, jump, "Jump", 0.07026373f, 0f);
            AddConditionTransition(movement, inAir, "FreeFall", 0.037536144f, 0.23041831f);
            AddExitTimeTransition(jump, inAir, 0.66366935f, 0.47048202f, 0.60876185f);
            AddConditionTransition(inAir, land, "Grounded", 0.09764749f, 0.08027425f);
            AnimatorStateTransition landBack = AddExitTimeTransition(land, movement, 0.39948252f, 0.4340285f, 0.36290926f);
            landBack.interruptionSource = TransitionInterruptionSource.Destination;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[TpsStarterAssetsAnimatorSetup] Starter Assets locomotion + jump graph wired into Character.controller");
        }

        private static AnimationClip LoadClip(string fbxName, string clipName) {
            return AssetDatabase.LoadAllAssetsAtPath(AnimDir + fbxName)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name, bool defaultValue) {
            if (controller.parameters.Any(p => p.name == name)) return;
            controller.AddParameter(new AnimatorControllerParameter {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = defaultValue,
            });
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name) {
            return sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        }

        private static BlendTree BuildBlendTree(AnimatorController controller, string name, params (AnimationClip clip, float threshold)[] children) {
            var tree = new BlendTree {
                name = name,
                blendParameter = "Velocity",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach ((AnimationClip clip, float threshold) in children)
                tree.AddChild(clip, threshold);
            return tree;
        }

        /// <summary>Swaps the state's motion and destroys the previous embedded blend-tree sub-assets.</summary>
        private static void ReplaceMotion(AnimatorState state, Motion newMotion) {
            Motion old = state.motion;
            state.motion = newMotion;
            if (old != newMotion) DestroyTreeRecursive(old as BlendTree);
        }

        private static void DestroyTreeRecursive(BlendTree tree) {
            if (tree == null) return;
            foreach (var child in tree.children)
                DestroyTreeRecursive(child.motion as BlendTree);
            Object.DestroyImmediate(tree, true);
        }

        private static void ClearTransitions(AnimatorState state) {
            foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
        }

        private static AnimatorStateTransition AddConditionTransition(AnimatorState from, AnimatorState to, string boolCondition, float duration, float offset) {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.offset = offset;
            t.AddCondition(AnimatorConditionMode.If, 0f, boolCondition);
            return t;
        }

        private static AnimatorStateTransition AddExitTimeTransition(AnimatorState from, AnimatorState to, float exitTime, float duration, float offset) {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.offset = offset;
            return t;
        }
    }
}
