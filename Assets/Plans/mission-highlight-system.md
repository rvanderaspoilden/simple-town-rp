# Overview
Add a visual highlight system for active mission targets in Unity (URP) by reusing the existing `OutlineRendererFeature`. The system will feature a "cozy pulse" animation and prioritize mission targets over standard hover highlights.

# Project Overview
- Game Title: Simple Town RPG
- High-Level Concept: Relaxing life-sim/job-sim where players complete various career-based missions.
- Players: Single player / Multiplayer (Mirror-based).
- Render Pipeline: URP.
- Screen Orientation: Landscape.

# Game Mechanics
## Core Gameplay Loop
- Accept jobs/missions.
- Interact with specific world objects (machines, bins, items).
- Complete steps to earn rewards.

## Controls and Input Methods
- Mouse interaction (Point & Click).
- Hover highlights for interactable objects.
- GPS/UI indicators for mission targets.

# UI
- The mission highlights will complement the existing `JobActiveTargetIndicator` (GPS) by providing direct world-space feedback on the target objects.

# Key Assets & Context
- `OutlineRendererFeature`: Screen-space outline pass.
- `PropHoverOutline`: Existing component for hover highlights.
- `CareerInteractableBase`: Base class for job-specific machines/interactables.
- `MissionItemBehaviour`: Component for mission-specific items.
- `JobClientManager`: Client-side mission state manager.

# Implementation Steps

## 1. Scene & Rendering Setup
- **New Layer**: Add a layer named `MissionHighlight` (e.g., Layer 7).
- **Renderer Configuration**: 
    - Add a second `OutlineRendererFeature` instance to the URP Renderer.
    - Set `Outline Layer` to `MissionHighlight`.
    - Configure it with a "Mission" color (e.g., warmer yellow/gold or soft white).
    - Set `Thickness` slightly higher than the default hover (e.g., 3-4 vs 2).
- **Camera Configuration**: Update `CameraManager` to include the `MissionHighlight` layer in its interaction raycast mask so that mission objects remain clickable.

## 2. Core Highlighting System
- **`MissionHighlightEffect`**: A new component responsible for swapping the layers of a GameObject's renderers to `MissionHighlight`. It will also manage state to avoid conflicts with hover outlines.
- **`MissionHighlightManager`**: A singleton script that listens to `JobClientManager` events (`JobOffered`, `JobStepAdvanced`, `JobFinished`). It will resolve mission `TargetId`s to scene objects and toggle the `MissionHighlightEffect`.

## 3. Mission Target Registry
- Create a client-side registry to map `TargetId` (strings) to `GameObjects`.
- Modify `CareerInteractableBase` to register/unregister itself in `Awake`/`OnDestroy`.
- Modify `MissionItemBehaviour` to register/unregister itself.

## 4. Animation & "Game Feel"
- **Pulse Animation**: Instead of per-object animation, `MissionHighlightManager` will update a Global Shader Property `_GlobalMissionOutlinePulse`.
- **Shader/Feature Modification**: Modify `OutlineRendererFeature.cs` to read from this global property to modulate thickness and color intensity. This ensures perfect synchronization and high performance.

## 5. Conflict Resolution
- Update `PropHoverOutline` to check if a mission highlight is already active on the object. If so, it should skip its own layer-swapping logic to prevent flickering.

# Implementation Detail: Files to Modify

## 1. `OutlineRendererFeature.cs`
- Add a `usePulse` toggle in `Settings`.
- In the `OutlinePass`, read `Shader.GetGlobalFloat("_MissionOutlinePulse")` to modulate properties if `usePulse` is active.

## 2. `CareerInteractableBase.cs` & `MissionItemBehaviour.cs`
- Implement a registration mechanism so the manager can find them by ID.

## 3. `CameraManager.cs`
- Add the `MissionHighlight` layer bit to `InteractionMask`.

# Verification & Testing
- **Visual Check**: Activate a mission targeting a Packaging Machine. Ensure it pulses softly with the dedicated color.
- **Hover Check**: Hover over the pulsing machine. The hover outline should not replace the mission outline (or they should blend gracefully).
- **State Check**: Change mission steps. The highlight should move to the next target immediately.
- **Cleanup Check**: Finish the mission. All highlights should disappear.
- **Performance**: Test with 20+ interactables in view to ensure no FPS drop.
