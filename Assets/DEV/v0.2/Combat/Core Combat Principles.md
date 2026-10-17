
## 2.1 Guards Are Combat States

The primary guards are:

- **Vom Tag**
    
- **Pflug**
    
- **Alber**
    
- **Ochs**
    
- **Iron Gate**
    

These are not merely idle poses.

They are the states through which combat flows.

A guard determines the player's current combat position, while selecting another guard determines the attack being performed.

Conceptually:

```
Current Guard
      ↓
Attack / Guard Transition
      ↓
Destination Guard
```

The destination guard is therefore important even when the transition itself is the primary offensive action.

---

## 2.2 The Important Part Is Passing Through Guards

The fundamental combat rule is **not** that transitions must be procedural.

Transitions may be:

- fully authored
    
- procedurally interpolated
    
- partially procedural
    
- authored with procedural corrections
    
- or some combination of these
    

The implementation can change.

The combat rule cannot:

> **Attacks are selected and expressed through movement between guards.**

An authored transition animation does not violate this principle.

For example:

```
Vom Tag
   ↓
   Pass_VomTag_ToAlber
   ↓
Alber
```

is still a guard-based attack even if the entire Zornhau motion is hand-authored. In reality,
the transition can end up being:

```
Vom Tag
   ↓
   Pass_VomTag_ToLangort
   ↓
   Pass_Langort_ToAlber
   ↓
Alber
```


---

## 2.3 No Conventional Attack System

The game should not be built around conventional:

- Light Attack
    
- Heavy Attack
    
- Attack 1
    
- Attack 2
    
- Combo Attack
    

inputs.

The player expresses intent by selecting or moving toward a guard.

The combat system determines the resulting transition.

This should make combat feel like controlling a swordsman rather than operating an animation menu.

---
