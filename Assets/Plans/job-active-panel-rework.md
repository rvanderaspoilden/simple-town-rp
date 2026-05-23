# Job Active Panel Rework Plan

## Project Overview
- **Game Title**: Simple Town RP (The Broz)
- **High-Level Concept**: A roleplay game where players perform various jobs (Delivery, Cleaning, etc.) in a city environment.
- **Players**: Multiplayer (Mirror).
- **Inspiration**: Modern mobile/web UI with clean, rounded aesthetic.
- **Tone / Art Direction**: Stylized, clean, user-friendly.
- **Render Pipeline**: URP.
- **Input System**: Legacy Input Manager.

## Game Mechanics
### Core Gameplay Loop
Players accept jobs, follow steps (Reach target, Pickup, Deliver, Sort), and earn rewards based on performance.
### Job HUD
A persistent UI panel that shows the current objective, progress, and relevant metrics (distance, time, count).

## UI Rework
The goal is to transform the existing `Job Active Panel` into a modern, dynamic component matching the provided reference image.

### Visual Mockup (based on image)
- **Background**: Rounded beige panel (`#EAD9C1`) with a subtle border or shadow.
- **Left Section**:
  - Icon Container: Circular or rounded square.
  - Icon: Job-specific icon (e.g., Package for Delivery).
- **Right Section**:
  - **Title**: Bold, dark brown text (`#3E2723`).
  - **Separator**: Thin horizontal line.
  - **Description**: Objective text (e.g., "Livrer le colis...").
  - **Dynamic Progress Bar**: 
    - Slider: Darker beige/brown fill.
    - Counter Text: "X/Y" or "120m" or "02:30".

### Dynamic Content Mapping
The panel will adapt based on the current job state:
1. **Counter Mode**: Used for steps like sorting or multiple deliveries. (Slider = Progress, Text = "Current/Total").
2. **Timer Mode**: Used when a job has an expiration. (Slider = Remaining Time %, Text = "mm:ss").
3. **Distance Mode**: Used for travel steps. (Slider = Distance progress or hidden, Text = "X m").

## Key Assets & Context
- **Scripts**:
  - `Assets/Scripts/Jobs/UI/JobActiveHUD.cs`: Main controller for the HUD.
- **Prefabs**:
  - `Assets/Resources/Prefabs/Managers/HUD Manager.prefab`: Contains the UI hierarchy.
- **Sprites**:
  - `Assets/Resources/Sprites/Shapes/Round10.png` (or similar) for the background.
  - `JobDefinition.Icon` for the left icon.

## Implementation Steps

### 1. Hierarchy Rework in `HUD Manager.prefab`
- **Modify `Job Active Panel`**:
  - Add `HorizontalLayoutGroup` (Padding: 15, Spacing: 15).
  - Add `IconContainer` (Fixed Size, e.g., 60x60).
    - Add `IconImage` (`UnityEngine.UI.Image`).
  - Add `ContentContainer` (`VerticalLayoutGroup`, Spacing: 5).
    - **Title** (`TMP_Text`, Bold, Size ~18).
    - **Separator** (`UnityEngine.UI.Image`, Height 1, Alpha 0.2).
    - **Description** (`TMP_Text`, Size ~14, Wrapping enabled).
    - **ProgressGroup** (`HorizontalLayoutGroup`, Spacing: 10, Alignment: Middle).
      - **ProgressBar** (`UnityEngine.UI.Slider`).
      - **ProgressText** (`TMP_Text`, Size ~14).

### 2. Script Update: `JobActiveHUD.cs`
- **Fields**:
  - Consolidate references to the new hierarchy.
  - Add `UnityEngine.UI.Image iconImage`.
  - Add `UnityEngine.UI.Slider progressBar`.
  - Add `TMP_Text progressText`.
- **Logic**:
  - `Render(state)`: 
    - Set Title and Description.
    - Set Icon from `state.Definition.Icon`.
    - Determine which "mode" to use for the progress group.
  - `Update()`:
    - If in **Timer Mode**, update the bar and text.
    - If in **Distance Mode**, update the text and optionally the bar.
    - If in **Counter Mode**, update the bar and text (handled via `OnSortProgress`).

### 3. Styling & Theming
- Apply colors matching the reference:
  - Background: Beige.
  - Title/Text: Dark Brown.
  - Bar Fill: Dark Brown.
  - Bar Background: Lighter Brown/Beige.

## Verification & Testing
- **Manual Test**: Start different jobs (Delivery, Sorting) and verify the panel updates correctly.
- **Edge Case**: Job with no icon (fallback to default).
- **Edge Case**: Long description text (verify wrapping and layout expansion).
- **Edge Case**: Timer reaching 0.
