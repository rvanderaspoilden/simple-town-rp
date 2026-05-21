# Project Overview
- Game Title: Simple Town
- High-Level Concept: A cozy town simulation with a stylized "Animal Crossing" or "Sims" look.
- Players: Single player
- Inspiration / Reference Games: Animal Crossing: New Horizons, The Sims 4.
- Tone / Art Direction: Cozy, vibrant, pastel-influenced, stylized.
- Target Platform: PC (StandaloneWindows64)
- Render Pipeline: Universal Render Pipeline (URP)

# Game Mechanics
## Core Gameplay Loop
The game features a day/night cycle that affects the visuals and mood of the town. The player explores the environment under different lighting conditions.
## Controls and Input Methods
Standard legacy input manager (as per project settings).

# UI
N/A for this task.

# Key Asset & Context
- **Polyverse Skies**: The primary skybox system.
- **MeteoManager.cs**: Controls the `timeOfDay` property of the skybox and the rotation of the sun/moon.
- **Sun Light / Moon Light**: Directional lights providing the main illumination.
- **Global Volume Profile**: Handles post-processing effects.

# Implementation Steps

## 1. Refine Polyverse Skies Materials
Adjust the Day and Night materials to match a "cozy" palette.
- **File**: `Assets/Resources/Materials/Skies/Day Sky.mat`
  - Set `_SkyColor` to a soft, vibrant blue.
  - Set `_SunColor` to a warm, peach/orange.
- **File**: `Assets/Resources/Materials/Skies/Night Sky.mat`
  - Set `_SkyColor` to a deep indigo/purple.
  - Set `_MoonColor` to a soft, pale blue.

## 2. Configure Directional Lights
Adjust the Sun and Moon light components.
- **GameObject**: `Sun Light`
  - Intensity: 1.2
  - Color: Warm white/yellow.
- **GameObject**: `Moon Light`
  - Intensity: 0.3
  - Color: Soft pale blue.

## 3. Post-Processing Overhaul
Configure the `Global Volume Profile` to enhance the "cozy" look.
- **Asset**: `Assets/Settings/PostProcessing/Global Volume Profile.asset` (Path to be verified if different)
- **Tonemapping**: Set to `ACES`.
- **Color Adjustments**: 
  - Saturation: +20 to +30 for vibrancy.
  - Contrast: +15 for clarity (currently negative).
  - Post Exposure: ~0.1-0.2.
- **Bloom**:
  - Intensity: 0.6
  - Threshold: 0.9
  - Scatter: 0.7
- **Vignette**:
  - Intensity: 0.2
  - Smoothness: 0.4
- **Depth of Field** (New):
  - Mode: `Gaussian`
  - `Start`: 10, `End`: 50 (Adjust to scene scale) to create a subtle "tilt-shift" miniature effect.

## 4. Lighting Environment
- Ensure `PolyverseSkies.updateLighting` is `true`.
- Update `RenderSettings` to use `Skybox` as the ambient source.

# Verification & Testing
- **Visual Check**: Run the game and observe the sky at different times (00:00, 06:00, 12:00, 18:00).
- **Smoothness**: Ensure the transition between day and night materials via `MeteoManager` is seamless.
- **Performance**: Verify that the added Depth of Field does not significantly impact frame rate on the target platform.
