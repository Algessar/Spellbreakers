# Spirits - Implementation Notes (first pass)

Status of what actually exists in code, as opposed to what `Spirits.md` is still deciding.

Companion document: `Spirits.md` remains the design source. This file records only the implementation,
so the two do not drift into contradicting each other.

## What is implemented

### Trees

Four element trees - Fire, Wind, Earth, Water - in `Assets/Scripts/Spirits/Core/`.

Rules follow the Classic WoW shape the brief asked to start from:

- **Points per tier**: five points spent in a tree open its next tier. Tier 1 is always open.
  `SpiritTree.PointsRequiredForTier`.
- **Ranks**: every talent has a rank count and a cost per rank; ranks are bought one at a time.
- **Prerequisites**: a talent may require another talent in the same tree at a given rank.
- **Respec**: points in one tree, or all of them, can be refunded in full.
- **Free choice**: points can be spent in any tree in any mixture. Nothing forces specialisation.

Content is authored in code (`SpiritCatalog`), following the pattern `CombatRules` already uses, so the
system runs with no assets wired up. `SpiritCatalog.Override` replaces it wholesale for tuning or tests.

### Points

Where Spirit points come from is still open in `Spirits.md` (shrines, home-base decoration, something
else). For now the controller grants a configurable starting budget, and `SpiritTreeController.GrantPoints`
is the seam the eventual reward source should call.

### Persistence

`SpiritProgressStore` writes `spirits.json` under `Application.persistentDataPath`. Loading is forgiving:
a missing or corrupt file produces a fresh progression instead of an exception, and talent ids that no
longer exist are dropped rather than failing the whole load.

### The tree window

`SpiritTreeUI` builds a Classic WoW shaped pane at runtime: one tab per element across the top, tiers top
to bottom, a detail pane on the right with the requirement reason spelled out, and a footer respec.

There is no prefab and no scene wiring. `SpiritTreeController` bootstraps itself with
`[RuntimeInitializeOnLoadMethod]`, so the window is available in every scene by pressing **P**
(gamepad **Select**). **Escape** closes it.

## What is deliberately not implemented

`Spirits.md` leaves three questions open, and none of them are guessed at here:

1. **Progression** - what an upgrade represents ("unlocking a new boon, improving an existing boon, or
   both") is unanswered. Each talent therefore carries a `BoonId` naming the effect it is *meant* to
   drive, and nothing reads it yet. `SpiritProgression.ActiveBoonIds` is the seam.
2. **Triggering** - what counts as a well-timed action. The document settles that it is not a combo but a
   perfect parry or a caught bind; the reward for either is still undecided.
3. **Point source** - shrines, home-base decoration, or something else.

The window's footer says so on screen, so a playtester does not spend points expecting an effect.

## Files

| Concern | File |
| --- | --- |
| Element identity | `Assets/Scripts/Spirits/Core/SpiritElement.cs` |
| Node data | `Assets/Scripts/Spirits/Core/SpiritTalent.cs` |
| Tree validation and tier rule | `Assets/Scripts/Spirits/Core/SpiritTree.cs` |
| Authored content | `Assets/Scripts/Spirits/Core/SpiritCatalog.cs` |
| Rules engine | `Assets/Scripts/Spirits/Core/SpiritProgression.cs` |
| Save file | `Assets/Scripts/Spirits/SpiritProgressStore.cs` |
| Window | `Assets/Scripts/Spirits/UI/SpiritTreeUI.cs` |
| Ownership, input, persistence | `Assets/Scripts/Spirits/UI/SpiritTreeController.cs` |

`Spellbreakers.Spirits` is a separate assembly with `noEngineReferences: true`, so the rules cannot
accidentally grow a Unity dependency. The window lives in `Spellbreakers.Gameplay` because it needs uGUI
and TextMeshPro.

## Tests

`Assets/Tests/EditMode/SpiritProgressionTests.cs` covers the rules (tier gating, prerequisites, rank
limits, refunds, respec) and validates the shipped content, including that every tree can actually be
fully ranked - the classic trap being a tier gate that its own prerequisite chain can never satisfy.
