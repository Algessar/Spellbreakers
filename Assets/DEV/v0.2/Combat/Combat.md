# Combat

The single reference for how combat works. This replaces the previously scattered set of documents in this
folder; their content is consolidated here so there is one place to look.

**Section numbers are load-bearing.** Several comments in `Assets/Scripts/Combat/` cite the design doc by
number ("doc 3.2", "section 5", "dev doc section 6"). Those headings are preserved exactly so the code
stays traceable. Do not renumber.

Status markers used throughout:

- **Implemented** - matches the code today.
- **Open** - a real decision still to be made.
- **Later** - acknowledged, deliberately postposed.

**Section 7 is intentionally absent.** Its source document is marked `[ai-ignore]`, so it was not merged and
has been left as its own file. Every other numbered section is preserved verbatim.

---

# Foundations

## 2.1 Guards Are Combat States

The primary guards are:

- **Vom Tag**
- **Pflug**
- **Alber**
- **Ochs**
- **Iron Gate**

Plus **Langort**, which is a pose attacks pass *through* rather than one a fighter holds (see 5).

These are not merely idle poses. They are the states through which combat flows.

A guard determines the player's current combat position, while selecting another guard determines the attack
being performed.

```text
Current Guard
     ->
Attack / Guard Transition
     ->
Destination Guard
```

The destination guard is therefore important even when the transition itself is the primary offensive action.

## 2.2 The Important Part Is Passing Through Guards

The fundamental combat rule is **not** that transitions must be procedural.

Transitions may be:

- fully authored
- procedurally interpolated
- partially procedural
- authored with procedural corrections
- or some combination of these

The implementation can change. The combat rule cannot:

> **Attacks are selected and expressed through movement between guards.**

An authored transition animation does not violate this principle.

```text
Vom Tag
   ->
   Pass_VomTag_ToAlber
   ->
Alber
```

is still a guard-based attack even if the entire Zornhau motion is hand-authored. In practice the transition
may actually end up being:

```text
Vom Tag
   ->
   Pass_VomTag_ToLangort
   ->
   Pass_Langort_ToAlber
   ->
Alber
```

## 2.3 No Conventional Attack System

The game should NOT be built around conventional Light Attack / Heavy Attack / Attack 1 / Attack 2 / Combo
Attack inputs.

The player expresses intent by selecting or moving toward a guard. The combat system determines the resulting
transition.

This should make combat feel like controlling a swordsman rather than operating an animation menu.

---

# Guards, Transitions and Sides

## 3.1 Complete Transition Space

Any guard should be able to transition to any other guard.

The transition table describes **how a particular transition is expressed**, not which transitions are
mechanically permitted.

`Vom Tag -> Pflug`, `Vom Tag -> Ochs`, and `Vom Tag -> Vom Tag` are all valid. The system should not
artificially restrict transitions simply because some pairings are less optimal.

Transitions differ primarily in:

- attack type
- line
- distance
- timing
- resulting position
- tactical usefulness

**Implemented:** an unauthored guard pair still resolves - to a synthesised plain traversal with **no damage
window**. Movement between any two guards is never forbidden, but no free attack is invented either.

## 3.2 Self-Transitions and Side Switches

Certain guards may support attacks that begin and end in the same guard.

Candidates:

```text
Vom Tag -> Vom Tag      = Zornhau

Pflug   -> Pflug        = Stichen

Ochs    -> Ochs         = Swerchau
```

These may alternatively be represented as side-switches:

```text
R Vom Tag -> L Vom Tag      = Zornhau

R Iron Gate -> L Iron Gate  = Mittelhau
```

**Status - partly decided.** The *concept* is required and implemented: the system must never assume that
destination guard differs from source guard. Today `Vom Tag -> Vom Tag` (Zornhau) is authored as a real
self-transition attack.

A **side switch is implemented as a traversal, not an attack**. Mirroring a guard moves the fighter to the
other leading side and does no damage, so it is always available even from a guard with no authored left-side
attack out of it. Whether a side switch *should* instead become an attack (as the `= Zornhau` framing above
suggests) is **Open**.

## 4. Guard Sides

*Previously `Guard Sides.md`; "currently only right-side guards exist" is no longer true.*

A guard's **side is the stance, not the side the sword happens to be on**. The guard counts as right-sided
when the **left foot is forward**.

This matters for Alber in particular: even though the sword is *on the left side*, Alber counts as right-sided
because the left foot is forward. (Put the right foot in front while keeping the pose and it becomes Iron
Gate.)

### Attack handedness

The mirrored pose of a guard determines the direction its cut travels:

```text
Right side Vom Tag -> Alber     cuts diagonally from right to left
Left  side Vom Tag -> Alber     cuts diagonally from left to right
Right side Alber   -> Iron Gate cuts horizontally left to right
Left  side Alber   -> Iron Gate cuts horizontally right to left
```

`TransitionDirection` (Oberhau, Mittelhau, Unterhau, Thrust) names the **line**, not the handedness.
Handedness comes from the side. A right-side Oberhau and its left-side mirror are the same `Oberhau` travelling
in opposite directions.

### Static parry side rule

A static (held) guard only intercepts a strike that arrives on the **opposite** side.

> If a strike is thrown from the right-side Vom Tag it cuts **left to right**, so the defender's blade is only
> in the way if they are holding the **left**-sided guard.

Worked example: `R Vom Tag -> Langort -> Alber` (an Oberhau/Zornhau) is blocked by **L Vom Tag**, and *not*
by R Vom Tag.

Implemented for **Vom Tag, Ochs and Alber**. A same-side guard resolves as a hit regardless of how favourable
the matchup row looks.

**Deliberately excluded: Iron Gate and Pflug.** Parrying a thrust is an active movement rather than a static
interception, and that rule is **Open**. Those guards therefore stay side-agnostic and behave exactly as they
did before sides existed.

The set is data, not hardcoded - `ParryTable.SetSideSensitive(guard, bool)`.

### Animator naming

Unity **renames `name.L` to `name_L`** when you type it into the Animator window, so the suffix is an
underscore, never a dot.

**Both sides are explicit** — there is no bare/default spelling:

- Guards layer: `VomTag_R`, `VomTag_L`, `Ochs_R`, `Ochs_L`, `Alber_R`, `Alber_L`, `Pflug_R`, `Pflug_L`,
  `IronGate_R`, `IronGate_L`
- Passes layer: `Langort` on the right, `Langort_L` on the left

`GuardIds.RightSideSuffix` / `LeftSideSuffix` are the single source of this; `ToStateName(GuardKey)` emits
the guard state name and parsing also tolerates a legacy `.L`.

> **Current mismatch, known and deferred:** the pass layer has not been renamed yet. Right-side passes still
> use the bare `Langort`, while the mirrored rows already ask for `Langort_L`, which does not exist. A
> left-side attack will therefore play no pass animation until the pass states are renamed. Guard poses are
> unaffected.

### Input

`SwitchMirroredGuard` swaps the current guard for its mirror. It is a normal guard request, so it obeys
commitment, queueing and reaction rules rather than teleporting the pose. The five guard buttons keep the
side currently held.

## 5. Guard Passes

**Langort is retained as a pass-only guard.** Combat motion may pass through Langort without necessarily
ending there.

```text
Vom Tag -> Langort -> Alber
```

or:

```text
Ochs -> Langort -> Pflug
```

More generally:

```text
FromGuard
   ->
CorrectionPose
   ->
TransitionPose
   ->
CorrectionPose
   ->
ToGuard
```

The pass represents a meaningful intermediate combat position rather than requiring every attack to have a
bespoke intermediate pose. Some transitions may instead pass through other guards depending on the attack and
distance.

**Terminology (used from here on):**

- **Guard Pose** - the single pose definition of the guard itself, e.g. VomTag.
- **Transition Pose** - the extreme extension passed through during a Hew. Currently only Langort.
- **Correction Pose** - an authored pass-through animation that smooths the motion and makes it look right.

**Status:** correction clips exist and are imported, but the runtime's pass layer currently realises a
transition as `FromGuard -> TransitionPose -> ToGuard`. The full correction chain above is **Later**.

Transition-pose candidates beyond Langort:

- **Transition-Schwerhau** - high Langort, blade flat horizontally, so a cut reads as a cut and not a thrust.
- **Transition-Hengen** - sword held angled above the head, usually over the shoulder.
- **Transition-Kron** - possibly triggered as a parry.



## 6. Current Transition Table

The authored attack language. Every row is an attack: the blade is live through the profile's damage window,
and the beat to punish is the Settle phase after that window closes. There are no "recovery" transitions.
Langort is never a source or destination.

| From      | To        | TransitionPose   | Meisterhau / Attack | CorrectionPose In | CorrectionPose Out |
| --------- | --------- | ---------------- | ------------------- | ----------------- | ------------------ |
| Vom Tag   | Vom Tag   | Langort          | Zornhau             | No                | No                 |
| Vom Tag   | Pflug     | Langort          | Stichen             | No?               | No?                |
| Vom Tag   | Alber     | Langort          | Zornhau             |                   |                    |
| Vom Tag   | Ochs      | Langort          | Zornhau             |                   |                    |
| Vom Tag   | Iron Gate | Langort          | Zornhau             |                   |                    |
| Ochs      | Vom Tag   | High Langort     | Schwerhau           |                   |                    |
| Ochs      | Pflug     | Langort (high?)  | Stichen             |                   |                    |
| Ochs      | Alber     | Langort          | Oberhau             |                   |                    |
| Ochs      | Iron Gate | Langort / Pflug* | Zornhau             |                   |                    |
| Alber     | Vom Tag   | Pflug / Langort* | Unterhau            |                   |                    |
| Alber     | Pflug     | Langort          | Stichen             |                   |                    |
| Alber     | Ochs      | Langort          | Unterhau            |                   |                    |
| Alber     | Iron Gate | Pflug            | Mittelhau           |                   |                    |
| Iron Gate | Vom Tag   | Pflug / Langort  | Unterhau            |                   |                    |
| Iron Gate | Pflug     | Pflug / Langort  | Stichen             |                   |                    |
| Iron Gate | Alber     | Pflug / Langort  | Mittelhau           |                   |                    |
| Iron Gate | Ochs      | Pflug / Langort  | Unterhau            |                   |                    |
| Pflug     | Vom Tag   | Langort          | Stichen             |                   |                    |
| Pflug     | Iron Gate | Langort          | Stichen             |                   |                    |
| Pflug     | Alber     | Langort          | Stichen             |                   |                    |
| Pflug     | Ochs      | Langort          | Stichen             |                   |                    |

`*` Some transitions depend on distance. [revision: transition selection depends in general on distance!]

**Status - implemented.** 21 authored rows over the five standing guards, matching this table. The table is
**not final**; it is a working combat-language definition and should be revised as playtest reveals what
actually feels correct.

**Mirroring (implemented):** every right-side row is automatically mirrored onto the left side, giving 42
authored rows. The mirror keeps the same technique name, attack line and damage window, and only changes the
side and the pass state (`Langort` -> `Langort_L`). The two sides therefore share one authored vocabulary
rather than drifting apart.

### Correction poses to animate

**To Langort** - all five authored:

- Corr-Ochs-Langort (done)
- Corr-VomTag-Langort (done)
- Corr-Alber-Langort (done)
- Corr-Pflug-Langort (done)
- Corr-IronGate-Langort (done)

**From Langort** - still to do:

- Corr-Langort-VomTag
- Corr-Langort-Pflug
- Corr-Langort-Alber
- Corr-Langort-Ochs
- Corr-Langort-IronGate

## 8. Attack Phases

Each transition is divided into three phases.

### ToPass

The weapon is being chambered/prepared. The fencer:

- moves toward the attack line
- establishes edge alignment
- commits to the transition
- creates a readable telegraph

This phase does not necessarily deal damage.

### Strike

The weapon passes through the active attack line. This is the primary damage window.

The blade should have meaningful velocity and a clear trajectory.

The attack passes through a Transition Pose, and may also pass through Correction Poses.

### Settle

The attack completes and the character arrives at the destination guard. This is the follow-through/recovery
portion. The resulting guard is now the fighter's new combat state.

A successful parry may interrupt this process and leave the attacker vulnerable.

**Implemented** as `PassPhase.None / ToPass / Strike / Settle`, derived from the transition's authored windows
rather than from Animator normalised time, so hit detection, IK and AI all read the same clock the decisions
were made on.

## 11. Directional Attack Types

The primary attack lines:

- **Oberhau** - downward diagonal
- **Mittelhau** - horizontal
- **Unterhau** - upward diagonal
- **Thrust / Stichen** - direct point attack

These describe the attack's line and motion. They should not be treated as independent "attack buttons" - they
are properties of the transition between combat states.

**Status: implemented, including left/right.** `TransitionDirection` carries the line; the guard's side
carries the handedness (section 4). A right-side Oberhau and its left-side mirror are the same line travelling
in opposite directions.

---

# Defence and Damage

## 12. Damage

Damage should eventually reflect the physical character of the attack rather than being entirely determined by
a static attack ID. Planned factors include weapon velocity, distance travelled, impact conditions and other
physical properties as the system develops.

The initial implementation should remain simple enough to validate the underlying combat.

> **A sword should deal damage because an attack successfully impacts a target, not because the player happens
> to be in an attack animation.**

A single transition must not repeatedly apply damage every frame while the weapon overlaps a target.

**Implemented in part:** `DeflectionResolver` turns timing quality, contact angle and blade speed into an
outcome gradient. Damage per hit is delivered once per contact.

## 13. Parrying

Parrying is a core combat mechanic. The player is rewarded for correctly reading and timing defensive
responses.

```text
Enemy attack
     ->
Player reads transition
     ->
Correct defensive timing
     ->
Blade contact / parry
     ->
Enemy loses initiative
     ->
Opening
```

A **perfect parry stagger** is planned: a perfect parry should create a meaningful tactical advantage rather
than a numerical damage reduction. **Open.**

### Two kinds of parry

Both are authored data in `ParryTable`, not emergent physics - a simulation here would be fragile and
unreadable.

1. **Static parry** - holding a guard as the attack arrives. Answered by the *guard* rows, and subject to the
   **side rule** in section 4.
2. **Transition parry** - meeting an attack with your own transition. Answered by the *transition* rows,
   which compare defence line against attack line and are **not** side-sensitive.

### Timing

A defender outside its parry window cannot parry at all, no matter how favourable the matchup. This is what
keeps "reward reading and timing" rather than "hold block". The guard's parry grace belongs to *arriving* in
the guard, not to holding it: after the grace period the same contact resolves as a hit. Krieg scales the
grace down by `GuardProfile.KriegParryGraceScale` (0.6).

### Outcomes

`Undefined` means the table has a hole; it is deliberately surfaced rather than silently defaulted, so a
completeness test catches it instead of it showing up as a balance bug.

- **HitBody** - the attacking blade reached the body.
- **Parry** - the defending blade met the attack in time and stopped it.
- **Bind** - both blades met and neither gave ground; the fight is in Krieg.
- **Deflect** - the attacking blade was pushed off line; the attacker is staggered.

---

# Space and Time

## Distance and Measure

Distance is a fundamental part of combat. The system distinguishes the conceptual HEMA phases:

- **Zufechten** - outside / approaching measure
- **Krieg** - close combat / blade contact
- **Ringen** - grappling

Not every phase needs to be implemented immediately. The important principle is that **distance changes which
attacks and transitions are useful**. Some transition descriptions therefore depend on distance.

**Implemented** as `Measure { OutOfRange, Zufechten, Krieg, Ringen }`, banded from the guard's tip reach.

Known distance rules:

- Being out of measure **feints** rather than rewarding flailing: the transition starts and aborts after a
  short beat.
- Krieg shortens transition duration and shrinks the parry grace.
- Vor (initiative) makes the same transition visibly quicker.

## 15. Targeting

The player uses target acquisition / lock-on when engaging opponents. Targeting should provide:

- a clear opponent focus
- directional duelling steering
- appropriate combat positioning
- reliable reacquisition

### Known issues

These are implementation bugs, not intended design behaviour.

- Pressing the stance/target button does not always acquire a target. This may have been caused by
  acquisition being relative to the camera, with FreeLook cam Orbits set too far away/too high. Setting lower
  values both made combat feel better and increased targeting accuracy.
- Manually un-targeting can make reacquisition difficult or impossible.
- Reacquisition may only become possible after the previous target dies.
- With multiple targetable objects, target selection can sometimes choose a farther object when a nearer
  target is expected.

### Related

Target acquisition only exists while the player is in combat; see *Combat State*. Pressing target out of
combat asks to engage when a hostile is already close, and otherwise does nothing.

## 21. Readability

Combat must remain visually readable. Guards should have distinct silhouettes. Attack transitions should
communicate direction, commitment, timing and approximate reach.

Possible supporting feedback:

- clear guard silhouettes
- blade glint during attack commitment
- directional blade trails during the active strike
- readable impact/contact feedback

These are presentation tools, not substitutes for readable animation.

## Combat and Environment

Combat should not be confined to featureless arenas. Positioning is part of swordsmanship.

The environment should affect distance, available attack angles, circling, footwork, escape routes, guard
selection and attack viability.

Useful combat environments include narrow gates, doorways, stairs, walls, ruins, uneven terrain, confined
interiors and open fields.

Level geometry is therefore part of the combat system rather than merely scenery surrounding it.

---

# Flow

## Combat State (implemented)

How "am I in combat?" is answered, and what changes when the answer changes.

This supersedes the sketch in the former `Stance.md`: free locomotion until an enemy comes into relevant
range, then combat stance.

### Why

The player used to be permanently in a guard. The Guards layer sat at weight one from the moment a scene
loaded, so there was no return to the ordinary locomotion idle, five buttons were spent on a stance the
player never chose, and the guard inputs could only ever do one thing because there was no state in which
they should do nothing.

### Rule

Entering and leaving combat are both decisions of distance, not of input.

```text
Nearest hostile > engage radius              -> free locomotion
Nearest hostile <= engage radius             -> in combat, guard up
Nearest hostile > disengage radius for N s   -> free locomotion again
```

Two radii rather than one is the point: a single boundary would flicker every time a fighter stepped over it.
The band between them is deliberately one-way - standing just outside the engage radius never starts a fight.

Defaults: engage at 18 m, disengage at 26 m, 2.5 s out of range. The engage radius sits inside the enemies'
own 25 m sight range, so the player is armed before anything is close enough to swing.

### What actually changes

In combat:

- the Guards layer is at full weight and held in the current guard
- guard inputs request transitions
- target locking is available
- attacking and parrying resolve normally

Out of combat:

- the Guards layer fades to zero, so the base layer owns the whole body: `Idle`, `Walk`, `Run`
- guard inputs are refused by `CombatHandler.RequestGuard` - the buttons are free
- targeting is dropped, so facing follows the movement direction again
- a pass in progress is cancelled rather than allowed to finish

`TargetingSystem` is the one place where input can start a fight: pressing target out of combat asks
`CombatEngagement.TryEngage`, which succeeds only if a hostile is already inside the engage radius.

### Open questions

- Whether the radii belong to the player, the enemy, or a shared engagement rule.
- Whether leaving combat should require distance at all, or also a timeout since the last blade contact.
- Whether an enemy should be able to disengage for the same reasons the player does.
- Whether the guard buttons should gain a non-combat use, which is the reason for freeing them.

## Combat Flow and Combos

The game should not rely on traditional canned combo strings. However, uninterrupted offensive flow is
desirable.

A planned **uninterrupted combo bonus** may reward maintaining successful transitions without losing offensive
momentum.

```text
Guard -> Attack -> New Guard -> Attack -> New Guard -> Attack
```

rather than:

```text
Attack 1 -> Attack 2 -> Attack 3
```

The player is chaining **combat transitions**, not executing a predetermined animation sequence.

### Perfect Actions and Triggered Effects

Triggered effects can happen on:

- **Parry** - parrying at a certain timing should produce a Perfect Parry.
- **Bind** - a bind should allow a quick follow-up if reacted to in time (similar to the Wind Waker shield
  mechanic, where letting the shield down and waiting for a strike yields a special roll or jump attack
  depending on stick direction). Effect of bind -> follow-up: **Undecided.** Suggestions: a Deflect +
  stagger combination.
- **Deflect** - **Undecided.**
- **Consecutive hits** - **Undecided.** Suggestions: increased stagger per hit, or rising damage to simulate
  continuous momentum from sword rotation.

*Triggered Effects and Spirits - the fantasy:* in addition to the physical advantages good play earns, you can
also trigger magical effects. See `Magic/Spirits.md`.

---

# Enemies

Enemies participate in the same fundamental combat language as the player, and should be understandable
through their physical actions. The player should be able to read enemy guard, attack preparation, attack
direction, commitment, recovery and openings.

Enemy difficulty should primarily come from timing, positioning, decision-making and readable but meaningful
behaviour, rather than inflated health pools or damage values.

## Non-humanoid enemies

The underlying combat system should not require enemies to be humanoid or to use the player's exact guard
system. The important abstraction is the **physical interaction**:

- attack direction and trajectory
- timing and active attack window
- attack reach and contact volume
- momentum / relative velocity
- defensive position and weapon/body placement
- impact, deflection and parry
- recovery and resulting openings

A humanoid enemy may use recognisable guards and guard transitions, while a non-humanoid enemy can use its own
physical states and attack patterns. The enemy does not need to behave like a swordsman for the same combat
interaction system to apply.

Examples:

- A **troll** with a club uses a restricted set of guards and transitions with different animations, reach and
  timing.
- A **wolf** uses bites, claw swipes and pounces, still readable through direction, timing and physical motion.
- A **dragon** uses claw swipes, tail sweeps, bites and other attacks; some map onto directional melee
  attacks, while others such as breath attacks need entirely different defensive responses.
- **Corrupted plants** use tendril lashes, thrusts, overhead strikes or area attacks without adopting
  humanoid guards.

Parrying and deflection should operate on the physical relationship between the incoming attack and the
defender rather than on the attacker's anatomy. A successful interaction may therefore exist on a gradient
rather than as "parried" or "not parried":

```text
good timing / angle -> clean deflection -> partial deflection -> knockback / stagger -> failed defence / hit
```

Mass, momentum, attack speed, contact angle and weapon/body geometry can influence the result. A sword might
cleanly redirect a human sword strike, partially deflect a troll's club, or be overwhelmed by a dragon.

> The player's **guard-transition system remains specialised to longsword combat**, while the underlying
> attack, contact, damage and deflection systems should be general enough to support very different enemy
> anatomies and attack styles.

The goal is not to translate every monster attack into a Lichtenauer technique, but for different creatures
to participate in the same **physical combat language** while expressing it through their own bodies.

**Open:** whether per-species parry relations are really needed, or whether the shared physical gradient above
covers them. `FencingArchetype` already lets each enemy select its own available guards and target guards.

---

# Implementation map

| Concern | File |
| --- | --- |
| Guards, sides, Animator state names | `Assets/Scripts/Combat/Core/GuardId.cs` |
| Authored transitions, mirroring | `Assets/Scripts/Combat/Core/TransitionTable.cs` |
| One transition's timing, distance, pose | `Assets/Scripts/Combat/Core/TransitionProfile.cs` |
| Parry relations (static + transition) | `Assets/Scripts/Combat/Core/ParryTable.cs` |
| Parry resolution, timing rule | `Assets/Scripts/Combat/Core/ParryResolver.cs` |
| Contact result and `HitInfo` | `Assets/Scripts/Combat/Core/CombatContact.cs` |
| The state machine | `Assets/Scripts/Combat/Core/FencingAgent.cs` |
| Damage gradient from physics | `Assets/Scripts/Combat/Core/DeflectionResolver.cs` |
| Measure bands, in-combat rule | `Assets/Scripts/Combat/Core/CombatAwareness.cs` |
| Rules entry point / overrides | `Assets/Scripts/Combat/Core/CombatRules.cs` |
| Enemy decision making | `Assets/Scripts/Combat/Core/Ai/FencingBrain.cs` |
| Guard/pass gating, rendering, input | `Assets/Scripts/Player/Combat/CombatHandler.cs` |
| Player-side combat state | `Assets/Scripts/Player/Combat/CombatEngagement.cs` |
| Hit detection and damage delivery | `Assets/Scripts/Player/Combat/WeaponDamage.cs` |
| Target lock | `Assets/Scripts/Player/Combat/TargetingSystem.cs` |
| Debug overlay | `Assets/Scripts/Combat/Debug/CombatVisualizer.cs` |
| Tests | `Assets/Tests/EditMode/` |

The core (`Assets/Scripts/Combat/Core/`) is engine-free and clock-free, and is covered by EditMode tests.
Combat timing is decided there, never by reading Animator normalised time.

---

# Open decisions

- Whether a side switch should become an attack (3.2) or stay a traversal.
- How thrusts are parried, which would bring Iron Gate and Pflug into the side rule (4).
- Perfect parry stagger, and the bind/deflect/consecutive-hit effects (13, Combat Flow and Combos).
- The full correction-pose chain in the runtime pass layer (5).
- Per-species parry relations versus a shared physical gradient (6).
- Whether a non-humanoid can use the guard-transition language at all, or only the physical layer (6).
- All Targeting bugs listed in 15.

# Systems refactor


