using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sim.Enums;
using UnityEngine;

namespace Sim.Building {
    public class PropsRenderer : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("Set all renderers which can be impacted by material changes")]
        [SerializeField]
        private Renderer[] renderersToModify;

        [Tooltip("Mesh renderers will be impacted by primary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] primaryStyleMeshRenderers;

        [Tooltip("Mesh renderers will be impacted by secondary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] secondaryStyleMeshRenderers;

        [Tooltip("Mesh renderers will be impacted by tertiary style of selected preset in configuration")]
        [SerializeField]
        private MeshRenderer[] tertiaryStyleMeshRenderers;

        [Tooltip("Is props hideable ??")]
        [SerializeField]
        private bool hideable = true;

        [Tooltip("Set to true means the props will be hide if it's between target and camera view")]
        [SerializeField]
        private bool interactWithCameraDistance;

        [Tooltip("Small representation of the props when interactWithCameraDistance is set to true")]
        [SerializeField]
        private GameObject foundationObj;

        private VisibilityStateEnum state = VisibilityStateEnum.SHOW;
        private VisibilityModeEnum mode = VisibilityModeEnum.AUTO;
        private PreviewStateEnum previewState = PreviewStateEnum.NONE;
        private Dictionary<Renderer, Material[]> defaultMaterialsByRenderer;

        // Driven by PropBehaviourBase via SetBuiltState. Defaults to "built" until told otherwise.
        private bool _isBuilt = true;

        private void Awake() {
            this.SetupDefaultMaterials();
        }

        private void Start() {
            this.SetVisibilityMode(VisibilityModeEnum.AUTO);
        }

        public void SetState(VisibilityStateEnum state) {
            this.state = state == VisibilityStateEnum.HIDE && this.hideable ? VisibilityStateEnum.HIDE : VisibilityStateEnum.SHOW;

            this.UpdateGraphics();
        }

        public void SetBuiltState(bool isBuilt) {
            _isBuilt = isBuilt;
            UpdateGraphics();
        }

        public void SetPreset(PropsPreset preset) {
            this.TryToApplyStyle(preset.Primary, this.primaryStyleMeshRenderers);
            this.TryToApplyStyle(preset.Secondary, this.secondaryStyleMeshRenderers);
            this.TryToApplyStyle(preset.Tertiary, this.tertiaryStyleMeshRenderers);

            this.UpdateGraphics();
        }

        private bool TryToApplyStyle(PropsStyle propsStyle, IEnumerable<MeshRenderer> meshRenderers) {
            if (!propsStyle.Enabled) return false;

            foreach (var meshRenderer in meshRenderers) {
                Material[] materialsToModify = this.defaultMaterialsByRenderer[meshRenderer];

                foreach (var material in materialsToModify) {
                    if (propsStyle.Material) {
                        material.CopyPropertiesFromMaterial(propsStyle.Material);
                    }

                    material.color = propsStyle.Color;
                }
            }

            return true;
        }

        /**
         * Keep all initial materials of the props to reset it at any moment
         */
        public void SetupDefaultMaterials() {
            this.defaultMaterialsByRenderer = this.renderersToModify.ToList().ToDictionary(x => x, x => x.materials);
        }

        public bool CanInteractWithCameraDistance() {
            return this.interactWithCameraDistance;
        }

        public bool IsHideable() {
            return this.hideable;
        }

        /// <summary>True when the prop is currently hidden (FORCE_HIDE on a hideable prop).</summary>
        public bool IsCurrentlyHidden() {
            return this.hideable && this.mode == VisibilityModeEnum.FORCE_HIDE;
        }

        public void SetVisibilityMode(VisibilityModeEnum mode) {
            this.mode = mode;

            this.UpdateGraphics();
        }

        public void SetPreviewState(PreviewStateEnum value) {
            this.previewState = value;
            this.UpdateGraphics();
        }

        public void UpdateGraphics() {
            VisibilityStateEnum visibility = this.state;

            if (this.mode == VisibilityModeEnum.FORCE_HIDE && this.hideable) {
                visibility = VisibilityStateEnum.HIDE;
            } else if (this.mode == VisibilityModeEnum.FORCE_SHOW) {
                visibility = VisibilityStateEnum.SHOW;
            }

            if (this.defaultMaterialsByRenderer == null) {
                this.SetupDefaultMaterials();
            }


            foreach (Renderer renderer in this.renderersToModify) {
                Material[] newMaterials = new Material[renderer.materials.Length];

                for (int i = 0; i < renderer.materials.Length; i++) {
                    if (this.previewState == PreviewStateEnum.NONE) {
                        if (visibility == VisibilityStateEnum.HIDE) {
                            newMaterials[i] = DatabaseManager.Instance.GetTransparentMaterial();
                        } else if (visibility == VisibilityStateEnum.SHOW) {
                            bool isUnbuilt = !_isBuilt;

                            if (isUnbuilt) {
                                newMaterials[i] = DatabaseManager.Instance.GetUnbuiltMaterial();
                            } else {
                                newMaterials[i] = this.defaultMaterialsByRenderer[renderer][i];
                            }
                        }
                    } else {
                        if (this.previewState == PreviewStateEnum.ERROR) {
                            newMaterials[i] = DatabaseManager.Instance.GetErrorMaterial();
                        } else {
                            newMaterials[i] = this.defaultMaterialsByRenderer[renderer][i];
                        }
                    }
                }

                renderer.materials = newMaterials;
            }

            // Optional
            if (foundationObj) {
                this.foundationObj.SetActive(visibility == VisibilityStateEnum.HIDE);
            }
        }

        // ── Construction reveal (Phase 2 sketch silhouette + Phase 4 vertical dissolve) ──
        //
        // Swaps the prop renderers to instances of Sim/ConstructionReveal that copy each
        // real material's base map/color, then reveals bottom-to-top as _Progress 0→1.
        // Driven over the network by PropBehaviourBase. EndConstructionReveal restores the
        // normal materials (UpdateGraphics: unbuilt ghost if cancelled, real if just built).

        private static readonly int ProgressId    = Shader.PropertyToID("_Progress");
        private static readonly int MinYId         = Shader.PropertyToID("_MinY");
        private static readonly int MaxYId         = Shader.PropertyToID("_MaxY");
        private static readonly int BaseMapId      = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId    = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId  = Shader.PropertyToID("_Color");

        private readonly List<Material> _revealInstances = new List<Material>();
        private bool _revealing;

        public bool IsRevealing => _revealing;

        public void BeginConstructionReveal() {
            if (renderersToModify == null || renderersToModify.Length == 0) return;
            if (this.defaultMaterialsByRenderer == null) this.SetupDefaultMaterials();

            Material template = Resources.Load<Material>("Materials/ConstructionReveal");
            if (template == null) {
                Debug.LogWarning("[PropsRenderer] ConstructionReveal material missing — reveal skipped");
                return;
            }

            // World-space vertical extent of the prop, so _Progress maps to bottom→top.
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer r in renderersToModify) {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            float minY = hasBounds ? bounds.min.y : transform.position.y;
            float maxY = hasBounds ? bounds.max.y : transform.position.y + 1f;

            EndConstructionReveal(restore: false); // clear any previous instances

            foreach (Renderer renderer in renderersToModify) {
                if (renderer == null) continue;
                Material[] defs = this.defaultMaterialsByRenderer[renderer];
                Material[] mats = new Material[defs.Length];
                for (int i = 0; i < defs.Length; i++) {
                    Material m = new Material(template);
                    Material src = defs[i];
                    if (src != null) {
                        if (src.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, src.GetTexture(BaseMapId));
                        if (src.HasProperty(BaseColorId)) m.SetColor(BaseColorId, src.GetColor(BaseColorId));
                        else if (src.HasProperty(LegacyColorId)) m.SetColor(BaseColorId, src.GetColor(LegacyColorId));
                    }
                    m.SetFloat(MinYId, minY);
                    m.SetFloat(MaxYId, maxY);
                    m.SetFloat(ProgressId, 0f);
                    mats[i] = m;
                    _revealInstances.Add(m);
                }
                renderer.materials = mats;
            }
            _revealing = true;
        }

        public void SetConstructionProgress(float progress) {
            if (!_revealing) return;
            float p = Mathf.Clamp01(progress);
            foreach (Material m in _revealInstances) if (m != null) m.SetFloat(ProgressId, p);
        }

        public void EndConstructionReveal() => EndConstructionReveal(restore: true);

        private void EndConstructionReveal(bool restore) {
            foreach (Material m in _revealInstances) if (m != null) Destroy(m);
            _revealInstances.Clear();
            _revealing = false;
            if (restore) this.UpdateGraphics();
        }
    }
}