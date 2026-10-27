# Combat State (implemented)

How "am I in combat?" is currently answered, and what changes when the answer changes.

This implements the model sketched in `Stance.md`: free locomotion until an enemy comes into relevant
range, then combat stance. It is a first pass and expects playtesting.

## Why

The player used to be permanently in a guard. The Guards layer sat at weight one from the moment a scene
loaded, so:

- there was no return to the ordinary locomotion idle - exploration was always performed holding a sword;
- five gamepad buttons were spent on a stance the player never chose;
- the guard inputs could only ever do one thing, because there was no state in which they should do nothing.

## Rule

Entering and leaving combat are both decisions of distance, not of input.

```
Nearest hostile > engage radius              → free locomotion
Nearest hostile ≤ engage radius              → in combat, guard up
Nearest hostile > disengage radius for N s   → free locomotion again
```

Two radii rather than one is the point: a single boundary would flicker every time a fighter stepped over
it. The band between them is deliberately one-way - standing just outside the engage radius never starts a
fight.

Defaults: engage at 18 m, disengage at 26 m, 2.5 s of being out of range. The engage radius sits inside
the enemies' own 25 m sight range, so the player is armed before anything is close enough to swing.

The rule itself is `CombatAwareness` in `Assets/Scripts/Combat/Core/`: engine-free, clock-free, and
covered by tests in `Assets/Tests/EditMode/CombatAwarenessTests.cs`.

## What actually changes

In combat:

- the Guards layer is at full weight and held in the current guard;
- guard inputs request transitions;
- target locking is available;
- attacking and parrying resolve normally.

Out of combat:

- the Guards layer fades to zero, so the base layer owns the whole body: `Idle`, `Walk`, `Run`;
- guard inputs are refused by `CombatHandler.RequestGuard` - the buttons are free;
- targeting is dropped, so facing follows the movement direction again;
- a pass in progress is cancelled rather than allowed to finish.

`TargetingSystem` is the one place where input can start a fight: pressing target out of combat asks
`CombatEngagement.TryEngage`, which succeeds only if a hostile is already inside the engage radius.
Pressing it with nobody around does nothing and leaves locomotion alone.

## Implementation

| Concern | File |
| --- | --- |
| The rule | `Assets/Scripts/Combat/Core/CombatAwareness.cs` |
| Player-side controller | `Assets/Scripts/Player/Combat/CombatEngagement.cs` |
| Guard/pass gating | `Assets/Scripts/Player/Combat/CombatHandler.cs` (`SetCombatActive`, `CombatActive`) |
| Target lock gating | `Assets/Scripts/Player/Combat/TargetingSystem.cs` |

`CombatEngagement` is created by `InputHandler` if the prefab does not already carry one, so no prefab
edit is required. Enemies keep `CombatHandler._startsInCombat = true` and are therefore unchanged: an
enemy is always in a guard. Making enemies sheathe their weapons until they perceive a target is a
separate, later decision.

## Open questions

- Whether the radii belong to the player, the enemy, or a shared engagement rule.
- Whether leaving combat should require distance at all, or also a timeout since the last blade contact.
- Whether an enemy should be able to disengage for the same reasons the player does.
- Whether the guard buttons should gain a non-combat use, which is the reason for freeing them.
