using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sim.Editor {
    /// <summary>
    /// Wires the crouch locomotion into Character.controller from the Synty Goblin
    /// Locomotion pack (Polygon rig, humanoid, retargeted onto the Shopper):
    ///   Crouch (bool) parameter,
    ///   Crouch state = 1D blend on Velocity: 0 Idle_Crouching / 1.5 Crouch_FwdStrafe_F
    ///   (1.5 = PlayerController.crouchSpeed — keep them in sync),
    ///   Movement → Crouch [Crouch], Crouch → Movement [!Crouch], 0.25s blends.
    /// Jump states are only reachable from Movement, so crouch never jumps by design
    /// (PlayerController also swallows Space to stand up instead).
    /// Idempotent — re-running rebuilds the same graph.
    /// </summary>
    public static class CrouchAnimatorSetup {
        private const string ControllerPath = "Assets/Resources/Animations/Personnage/Character.controller";
        private const string AnimDir = "Assets/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral/";

        // Must match PlayerController.crouchSpeed so the walk clip plays at full rate
        // exactly at crouch move speed.
        private const float CrouchWalkThreshold = 1.5f;

        [MenuItem("Tools/TPS/Setup Crouch Animator (Goblin Locomotion)")]
        public static void Setup() {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) {
                Debug.LogError($"[CrouchAnimatorSetup] Controller not found at {ControllerPath}");
                return;
            }

            AnimationClip crouchIdle = LoadClip("Idles/A_POLY_GBL_Idle_Crouching_Neut.fbx");
            AnimationClip crouchWalk = LoadClip("Locomotion/Crouch/A_POLY_GBL_Crouch_FwdStrafe_F_Neut.fbx");
            if (!crouchIdle || !crouchWalk) {
                Debug.LogError("[CrouchAnimatorSetup] Goblin Locomotion clips not found — is the pack imported?");
                return;
            }

            EnsureBoolParameter(controller, "Crouch");

            AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
            AnimatorState movement = FindState(baseLayer, "Movement");
            if (movement == null) {
                Debug.LogError("[CrouchAnimatorSetup] 'Movement' state not found in Base Layer");
                return;
            }
            Vector3 mvPos = baseLayer.states.First(s => s.state == movement).position;

            AnimatorState crouch = FindState(baseLayer, "Crouch")
                                   ?? baseLayer.AddState("Crouch", mvPos + new Vector3(0f, 160f, 0f));
            ReplaceMotion(controller, crouch, BuildBlendTree(controller, "Crouch",
                (crouchIdle, 0f), (crouchWalk, CrouchWalkThreshold)));

            // Rewire only the Movement ↔ Crouch pair (keep jump/action transitions intact).
            foreach (var t in movement.transitions.Where(t => t.destinationState == crouch).ToArray())
                movement.RemoveTransition(t);
            foreach (var t in crouch.transitions.ToArray())
                crouch.RemoveTransition(t);

            AnimatorStateTransition toCrouch = movement.AddTransition(crouch);
            toCrouch.hasExitTime = false;
            toCrouch.hasFixedDuration = true;
            toCrouch.duration = 0.25f;
            toCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");

            AnimatorStateTransition toStand = crouch.AddTransition(movement);
            toStand.hasExitTime = false;
            toStand.hasFixedDuration = true;
            toStand.duration = 0.25f;
            toStand.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[CrouchAnimatorSetup] Crouch state wired into Character.controller (Goblin Locomotion clips)");
        }

        private static AnimationClip LoadClip(string relativeFbxPath) {
            return AssetDatabase.LoadAllAssetsAtPath(AnimDir + relativeFbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name) {
            if (controller.parameters.Any(p => p.name == name)) return;
            controller.AddParameter(new AnimatorControllerParameter {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false,
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
        private static void ReplaceMotion(AnimatorController controller, AnimatorState state, Motion newMotion) {
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
    }
}
