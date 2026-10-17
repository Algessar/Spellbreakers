[obsolete] [created by Unity Assistant]

# Project Overview
- **Game Title**: Spellbreakers
- **High-Level Concept**: A grounded Historical European Martial Arts (HEMA / Liechtenauer)-inspired tactical swordplay game where transitions between guards are the attacks themselves—there are no detached "attack animations".
- **Players**: Single player (Player vs AI / Training Dummy) with lock-on targeting and directional duelling steering.
- **Inspiration / Reference Games**: *Hellish Quart*, *Bushido Blade*, *For Honor*, Joachim Meyer & Johannes Liechtenauer historical treatises.
- **Tone / Art Direction**: Authentic, weighted medieval fencing with readable silhouettes, crisp edge alignment, and diegetic combat feedback.
- **Target Platform**: Standalone PC (Windows 64-bit).
- **Screen Orientation / Resolution**: Landscape 1920x1080 (16:9).
- **Render Pipeline**: Universal Render Pipeline (URP).

---

# Game Mechanics

## Core Gameplay Loop
1. **Distance & Measure**: Duelists navigate between *Zufechten* (out of measure), *Krieg* (close measure / blade contact), and *Ringen* (grappling).
2. **Intent & Line of Attack**: Requesting a guard change from a current guard (e.g., Vom Tag, Pflug, Alber, Ochs, Langort, Iron Gate) launches a transition along an attack line (`Oberhau`, `Mittelhau`, `Unterhau`, `Thrust`) or recovery.
3. **Phase-Driven Execution**:
   - `ToPass` (Chamber / Preparation): The fencer winds their blade into position, orienting the edge along the cutting plane.
   - `Strike` (Active Damage Window): The blade rapidly accelerates through the line of attack, passing through maximum extension (Apex / Pass pose) with an active swept collision volume.
   - `Settle` (Follow-through & Recovery): The momentum decelerates as the fencer arrives in the destination guard, vulnerable to counter-attacks if parried.
4. **Parry & Contact Resolution**: Blade collisions test parry timing, guard alignment, and initiative (*Vor* vs *Nach*) to resolve into clean hits, parries, binds, or deflects.

## Controls and Input Methods
- **Guard Selection**: Dedicated directional keys/buttons (e.g., Vom Tag = High Right, Ochs = High Left, Pflug = Low Right, Alber = Low Center, Iron Gate = Low Left).
- **Targeting / Lock-on**: Toggle lock-on focus towards nearby opponents.
- **Footwork / Locomotion**: Directional strafing and in-guard forward/backward pacing driven by `Movement` and `TargetingSystem`.

---

# UI & Readability Design
- **Diegetic Readability**:
  - **Silhouette Clarity**: Exaggerated, distinct body postures for each guard so opponents can read the enemy's stance at a distance.
  - **Pre-Strike Glint**: A subtle blade edge sheen/specular flash during the `ToPass` chambering phase indicating attack initiation.
  - **Cutting Arc Ribbon Trail**: Directional trailing mesh along the live cutting edge active exclusively during `PassPhase.Strike`, mapping the exact path and plane of the blade (`Oberhau` = downward diagonal, `Mittelhau` = horizontal, `Unterhau` = upward diagonal, `Thrust` = focused point streak).
- **HUD & Debug Overlay**:
  - Existing `FencingDebugView` (toggled with F1) remains the authoritative debugging HUD, displaying active guard, progress (`Progress01`), phase, and parry grace.

---

# Key Asset & Context

### 1. What Must Be Authored Specifically (Static Poses & Data)
Constrained by the rule that guards and transitions are static poses rather than full 30+ bespoke mocap clips:
1. **The 6 Canonical Guard Poses** (Humanoid static 1-frame or looped idle poses):
   - `NEW_G_VomTag`: High roof guard, hilt over right shoulder, blade pointing 45° back-up, coiled torso.
   - `NEW_G_Ochs`: The Ox, high forward point, hilt defending the forehead, point threatening opponent's face.
   - `NEW_G_Pflug`: The Plough, low hip guard, point elevated forward, hands defending flank.
   - `NEW_G_Alber`: The Fool, point dropped low toward ground, chest baiting attack.
   - `NEW_G_Langort`: The Long Point, arms extended forward, blade along the central line.
   - `NEW_G_IronGate`: Low central defense, hilt held low and centered.
2. **The 4 Directional Apex / Pass Poses** (Authored static poses representing maximum extension per attack line):
   - `Pass_Oberhau`: Downward diagonal cut apex (blade extended forward-down at ~45°, torso tilted forward, right shoulder dropped).
   - `Pass_Mittelhau`: Horizontal cut apex (blade horizontal across torso level, hips twisted into the cut).
   - `Pass_Unterhau`: Upward diagonal cut apex (blade slicing from below, rising point, body opening upward).
   - `Pass_Thrust`: Direct long-point lunge (arms locked forward, maximum tip reach, linear shoulder alignment).
   *Design Impact*: Replaces the single generic Langort pass with 4 distinct silhouettes that cover all 18+ attacks without authoring individual pairwise animations.
3. **Transition Motion Curves (Scriptable/Authored Curves)**:
   - `StrikeTimingCurve`: Non-linear ease curve (slow start during `ToPass`, explosive acceleration through `Strike`, smooth ease-out in `Settle`).
   - `ArcDisplacementCurve`: Normal displacement along the cutting arc normal, peaking at the apex.

### 2. What Is Handled Procedurally (Code & Rigging)
1. **Three-Point Pose Blending Engine (`CombatHandler`)**:
   - Instead of a naive 2-pose crossfade, transitions execute a 3-stage blend:
     - `Stage 1 (0 -> DamageWindowStart)`: Interpolates from `From` Guard Pose to Directional `Apex` Pass Pose.
     - `Stage 2 (DamageWindowStart -> DamageWindowEnd)`: Holds/sweeps through the active Apex with full extension and velocity.
     - `Stage 3 (DamageWindowEnd -> 1.0)`: Interpolates from `Apex` Pass Pose into `To` Guard Pose.
2. **Procedural Non-Linear Timing Remapping**:
   - Maps normalized combat time `Agent.Progress01` through an easing function (Cubic Ease-In / Ease-Out or authored curve) to ensure visual sword acceleration matches fencing biomechanics (slow chamber -> explosive snap -> settling deceleration).
3. **Procedural Arc Curvature (Tip / Weapon Offset)**:
   - Calculates a procedural parabolic / Bezier outward arc displacement vector perpendicular to the chord connecting Guard A and Guard B.
   - Prevents the sword from clipping through the duelist's own torso or traveling along an unnatural flat chord.
4. **Procedural Spine & Torso Dynamics**:
   - Dynamically adds pitch, yaw, and roll to the Spine/Chest bones in `LateUpdate` or `OnAnimatorIK` matching the cut direction:
     - `Oberhau`: Downward torso pitch + forward lean.
     - `Mittelhau`: Torso rotational yaw follow-through.
     - `Unterhau`: Slight upward chest lift.
5. **Procedural Visual Telegraphing**:
   - `BladeTelegraphVFX`: Activates a lightweight URP TrailRenderer on the blade edge strictly during `PassPhase.Strike`, with arc color and width mapped to cut velocity.
   - `BladeGlint`: Triggers a brief shader flash / particle glint on the sword tip at the transition commitment point (`PassPhase.ToPass` completion).

---

# Implementation Steps

### Step 1: Author Directional Apex Pass Poses & Animator State Setup
- **Files**:
  - `Assets/Animation/HumanoidAvatar/Controller_Masks/Raine_Humanoid.controller`
  - Model FBX / Animation Clips for the 4 directional passes:
    - `Pass_Oberhau`
    - `Pass_Mittelhau`
    - `Pass_Unterhau`
    - `Pass_Thrust` (utilizing or refining `NEW2_Pass_Langort`)
- **Actions**:
  1. Set up states in the `Passes` layer of `Raine_Humanoid.controller` for each directional apex: `Oberhau`, `Mittelhau`, `Unterhau`, `Thrust`.
  2. Ensure the `Passes` layer has an avatar mask or upper body override consistent with the character rig, allowing lower body locomotion blending.
- **Dependencies**: Existing `Raine_Humanoid.controller`.

### Step 2: Extend Transition Data & Directional Mapping
- **Files**:
  - `Assets/Scripts/Combat/Core/TransitionProfile.cs`
  - `Assets/Scripts/Combat/Core/TransitionTable.cs`
- **Actions**:
  1. In `TransitionProfile`, map `PassStateName` automatically based on `Direction` (e.g., `Oberhau` -> `"Oberhau"`, `Mittelhau` -> `"Mittelhau"`, `Unterhau` -> `"Unterhau"`, `Thrust` -> `"Thrust"`).
  2. Add an authored timing curve reference or non-linear evaluation method `EvaluateVisualProgress(float progress01)`.
  3. Update `TransitionTable.CreateDefault()` so each attack profile points to its corresponding directional pass state name rather than hardcoding `"Langort"`.
- **Dependencies**: Step 1.

### Step 3: Implement Three-Point Pose Interpolation & Non-Linear Scrubbing in `CombatHandler`
- **Files**:
  - `Assets/Scripts/Player/Combat/CombatHandler.cs`
- **Actions**:
  1. Revamp `RealizeTransition()`:
     - Calculate non-linear mapped progress `t = profile.EvaluateVisualProgress(Agent.Progress01)`.
     - In the first half of the transition (`t < 0.5`), crossfade weight from the source guard (`From`) into the directional pass state on `_passesLayer`.
     - In the second half (`t >= 0.5`), begin crossfading the `Guards` layer underneath toward the destination guard (`To`), smoothly fading out the pass layer as progress approaches 1.0.
  2. Eliminate the abrupt jump at transition completion by ensuring layer weights and normalized times settle smoothly into `Agent.Current`.
- **Dependencies**: Step 2.

### Step 4: Implement Procedural Cutting Arc & Spine Dynamics
- **Files**:
  - `Assets/Scripts/Player/Combat/ProceduralSwordMotion.cs` (New script or integrated with `CombatHandler`)
- **Actions**:
  1. Read active transition direction (`Oberhau`, `Mittelhau`, `Unterhau`, `Thrust`) and current progress from `CombatHandler.Agent`.
  2. In `LateUpdate`, compute a procedural arc offset vector (parabolic curve peaking at `progress01 = 0.5`) and apply subtle rotational tilt to the weapon bone/hands to ensure the true cutting edge leads the trajectory.
  3. Apply dynamic torso follow-through (Spine pitch/yaw spring-damper) proportional to the cut direction to give physical weight to swings.
- **Dependencies**: Step 3.

### Step 5: Implement Diegetic Visual Telegraphing & Blade Trail
- **Files**:
  - `Assets/Scripts/Combat/BladeTrailEffect.cs` (New component on `Blade` / `WeaponDamage`)
  - Materials / Shaders in `Assets/Materials/` (URP Unlit trail material with soft edge fade)
- **Actions**:
  1. Attach a `TrailRenderer` or procedural ribbon generator to the `Blade` (between `_base` and `_tip`).
  2. Enable blade ribbon emissions strictly when `CombatHandler.Phase == PassPhase.Strike`.
  3. Emit a momentary gleam / glint particle or material property flash during `PassPhase.ToPass` to telegraph committed attacks clearly to human players.
- **Dependencies**: Step 3, Step 4.

### Step 6: Validate in SCENE_FencingLab & Run Unit Tests
- **Files**:
  - `Assets/Tests/EditMode/TransitionTableTests.cs`
  - `Assets/Tests/EditMode/FencingAgentTests.cs`
  - `Assets/World/Scenes/SCENE_FencingLab.unity`
- **Actions**:
  1. Run EditMode test suite to guarantee `FencingAgent`, `TransitionTable`, and parry logic remain 100% compliant.
  2. Open `SCENE_FencingLab` and visually inspect transitions from each guard to every legal target.
  3. Verify Oberhau cuts downwards with an authoritative arc, Mittelhau slices horizontally without clipping the torso, Unterhau sweeps upwards, and Thrust drives forward along the line.
- **Dependencies**: Steps 1–5.

---

# Verification & Testing

### 1. Automated Unit Tests
- Execute all existing EditMode tests (`FencingAgentTests`, `TransitionTableTests`, `ParryResolverTests`) via Test Runner to guarantee combat timing and deterministic rules are untouched.
- Add test cases in `TransitionTableTests` ensuring all default attacks are mapped to valid directional pass states (`Oberhau`, `Mittelhau`, `Unterhau`, `Thrust`).

### 2. Visual & Kinetic Inspection in Editor / Play Mode
- **Silhouette Test**: In `SCENE_FencingLab`, test every guard transition with character stationary:
  - From Vom Tag -> Alber (Oberhau): Confirm the blade arches over and down, torso leans into cut, blade trail traces clean vertical/diagonal plane.
  - From Pflug -> Ochs (Mittelhau): Confirm blade cuts horizontally across the plane without inward arm collapse.
  - From Alber -> Vom Tag (Unterhau): Confirm the blade scoops upward from low to high.
  - From any guard -> Langort (Thrust): Confirm straight, linear point delivery without lateral wobble.
- **Locomotion Blending Test**: Execute guard transitions while strafing left/right and moving forward/backward; confirm upper body transition blends seamlessly with lower body locomotion without broken hips or feet sliding.
- **Telegraph Readability Test**: Stand at Zufechten/Krieg against `ENEMY_Fencer`; observe enemy attack chambering glint and confirm whether human reaction can distinguish cut direction within 200–300ms before strike impact.
