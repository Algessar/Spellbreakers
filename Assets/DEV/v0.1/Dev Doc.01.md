[obsolete]

# Spellbreakers



## 1. Game Overview

**Spellbreakers** is a third-person action-adventure game built around a simplified, highly readable interpretation of historical longsword combat inspired by the **Lichtenauer tradition**.

The central combat concept is deliberately unusual:

> **There are no conventional attack animations. Attacks are transitions between guards.**

The player does not select an attack animation and wait for it to play. Instead, the combat system is based on a small set of recognizable guards and transitions between them. The character's body, weapon and attack motion are the result of moving from one combat state to another.

The overall game combines:

- Lichtenauer-inspired longsword combat
    
- Deliberate positioning, timing and spacing
    
- Fast, readable action
    
- Zelda-like exploration
    
- Relatively simple environmental puzzles
    
- A bright-ish, grounded medieval setting
    
- 3D environments designed around movement and combat
    
- Difficulty somewhere between classic **Zelda** and **Dark Souls**
    

The goal is **not** to make a historically exhaustive fencing simulator.

Historical swordsmanship is the foundation for the combat language, while the actual game is designed around readability, responsiveness and fun.

---

# 2. Core Design Pillars

## 2.1 Combat Through Guards

The combat system is built around a small number of recognizable guards rather than a large collection of independent attacks.

The primary guards are:

- **Vom Tag**
    
- **Pflug**
    
- **Alber**
    
- **Ochs**
    
- **Langort**
    

These are not merely idle poses.

They are the **states from which combat actions emerge**.

An attack is fundamentally a transition:

```text
Guard A
   ↓
Guard B
```

The transition itself represents the strike.

This makes the combat system fundamentally different from a conventional animation-driven action game.

---

## 2.2 No Conventional Attack Animations

There should be no traditional:

```text
Attack 01 → animation → damage window → recovery
```

model.

Instead:

```text
Current Guard
      ↓
Intent / Input
      ↓
Guard Transition
      ↓
Resulting Guard
```

The character is continuously interpolated between meaningful combat poses.

This keeps the combat system:

- visually readable
    
- state-driven
    
- responsive
    
- composable
    
- relatively small in terms of authored animation
    

The important thing is the **combat state and transition**, not a collection of bespoke attack animations.

---

# 3. Player Combat

## 3.1 No Dedicated Attack Button

The player should **not** need a conventional collection of:

- Light Attack
    
- Heavy Attack
    
- Attack 1
    
- Attack 2
    
- Combo Attack
    
- etc.
    

Combat actions emerge from **guard changes and player positioning**.

The player's input expresses intent, while the guard system determines the resulting movement.

This should make the combat feel more like controlling a swordsman than operating an animation menu.

---

## 3.2 Timing

Timing is a major part of combat.

A successful player should learn to:

- read an opponent's guard
    
- recognize an incoming transition
    
- choose an appropriate response
    
- control distance
    
- exploit openings
    
- attack at the correct moment
    

Combat therefore relies more heavily on **reading and timing** than on memorizing long button combos.

---

## 3.3 Parrying

Parrying is an important part of the combat language.

The system should reward correctly timed defensive actions rather than simply allowing the player to hold a permanent block.

A successful defensive interaction should create a meaningful change in the combat situation:

```text
Enemy attack
     ↓
Player reads attack
     ↓
Correct defensive response
     ↓
Contact / parry
     ↓
Opening
```

The exact implementation can evolve, but the important design principle is that **timing and reading the opponent matter**.

---

# 4. Combat and Space

Combat is not intended to happen inside featureless circular arenas.

**Positioning is part of swordsmanship.**

The environment should influence:

- distance
    
- available attack angles
    
- circling
    
- footwork
    
- flanking
    
- guard selection
    
- escape routes
    

Examples of useful environmental constraints include:

- narrow gates
    
- doorways
    
- stairs
    
- ruins
    
- walls
    
- uneven terrain
    
- confined interiors
    

A fight in an open field should therefore feel different from a fight in a narrow passage.

### Level geometry is part of the combat system.

The environment should create tactical situations rather than merely provide scenery around combat.

---

# 5. Enemy Combat

Enemies should participate in the same fundamental combat language as the player.

The goal is not simply:

```text
Enemy has 100 HP
Player deals 10 damage
```

Combat should instead be about:

- guards
    
- distance
    
- timing
    
- attacks
    
- defensive responses
    
- openings
    
- positioning
    

Enemy behavior should therefore be understandable through their physical actions.

The player should be able to **read what an enemy is doing**.

---

# 6. Animation Philosophy

The project deliberately avoids making authored attack animations the foundation of combat.

The desired model is:

```text
Combat logic
    ↓
Deterministic combat state
    ↓
Guard / transition
    ↓
Character pose
    ↓
Physical realization
```

The important distinction is:

**Combat decisions should determine the movement.**

Animation/physics systems should realize those decisions rather than secretly deciding combat behavior.

This allows the game to experiment with physical or procedural character realization without making the gameplay dependent on a giant collection of animation clips.

---

# 7. Exploration

Outside combat, Spellbreakers should have a strong **Zelda-like exploration structure**.

The player should primarily navigate the world through:

- environmental observation
    
- traversal
    
- discovering paths
    
- interacting with locations
    
- finding secrets
    
- solving relatively simple environmental problems
    

The exploration should not become a puzzle-game abstraction where every room exists primarily to present a logic puzzle.

The desired balance is closer to:

**Zelda exploration + fast/simple God of War-style environmental interaction.**

The player should generally be able to understand what the world is asking them to do without excessive abstraction.

---

# 8. World and Setting

The setting is a **bright-ish medieval world with a grounded historical foundation**, broadly inspired by the **13th–14th century**.

The world should feel recognizably medieval rather than generic fantasy.

Historical grounding provides the visual and physical foundation, while the game is still free to use fantasy elements where appropriate.

The intended atmosphere is not relentlessly grim.

The world can be:

- beautiful
    
- bright
    
- dangerous
    
- ancient
    
- mysterious
    

without becoming either a grimdark setting or a completely sanitized fantasy world.

---

# 9. Visual Direction

The game should make use of **actual 3D environments and vistas** rather than relying heavily on flat background scenery.

Large-scale environments are desirable because exploration should allow the player to understand the surrounding world spatially.

At the same time, environments need to remain practical for a small development team.

The important principle is:

> The world should feel like a connected physical place rather than a sequence of disconnected combat rooms.

---

# 10. Difficulty

The intended difficulty sits approximately between:

**Classic Zelda ← Spellbreakers → Dark Souls**

It should be challenging enough that combat mastery matters, but it should not require the extreme punishment or precision associated with Souls-like design.

Failure should teach the player something.

A player who understands:

- guards
    
- distance
    
- timing
    
- enemy behavior
    
- positioning
    

should become substantially better at the game.

The difficulty should come primarily from **understanding and executing the combat system**, rather than from inflated enemy statistics.

---

# 11. Intended Player Experience

The desired feeling is:

> **"I'm actually fighting this person with a sword."**

Rather than:

> "I'm pressing the correct attack button for the current combo."

The player should feel like they are:

- reading an opponent
    
- choosing a guard
    
- controlling distance
    
- committing to attacks
    
- reacting to attacks
    
- exploiting openings
    
- physically moving around another swordsman
    

The combat should therefore feel closer to a **duel** than a conventional action-game DPS rotation.

---

# 12. Core Gameplay Loop

At the highest level:

```text
Explore
  ↓
Discover locations / enemies / problems
  ↓
Engage with the environment
  ↓
Fight when necessary
  ↓
Read enemies and master combat
  ↓
Overcome obstacles
  ↓
Continue exploring
```

Combat and exploration should reinforce each other rather than feeling like two completely separate games.

---

# 13. What Is Fundamental

These are the features that define Spellbreakers and should be protected during development:

### Combat

- Lichtenauer-inspired longsword combat
    
- Small set of meaningful guards
    
- Vom Tag
    
- Pflug
    
- Alber
    
- Ochs
    
- Langort
    
- Guard transitions as attacks
    
- No conventional attack-animation system
    
- Timing-based combat
    
- Parrying
    
- Distance and positioning
    
- Readable enemy behavior
    
- Terrain affecting fights
    

### Exploration

- Zelda-inspired exploration
    
- Physical 3D environments
    
- Environmental interaction
    
- Relatively simple puzzles
    
- Discoverable paths and locations
    
- Large enough spaces to create meaningful navigation
    

### World

- Bright-ish medieval atmosphere
    
- 13th–14th-century inspiration
    
- Grounded medieval visual language
    
- Fantasy elements without abandoning the medieval foundation
    

### Difficulty

- More demanding than casual Zelda
    
- Less punishing than Dark Souls
    
- Skill comes primarily from understanding the systems
    

---

# 14. What Is _Not_ the Core

The following should not become requirements that dictate the design:

- A huge library of bespoke attack animations
    
- Conventional combo systems
    
- Animation-driven combat logic
    
- Excessive RPG stat complexity
    
- Souls-like difficulty for its own sake
    
- Puzzle-heavy dungeon design
    
- Historically perfect simulation
    
- Technology determining gameplay design
    

Technical systems can change.

The combat language should not.

---

# 15. Development Priority

When making implementation decisions, prioritize:

1. **Combat feel**
    
2. **Guard/transition clarity**
    
3. **Timing and responsiveness**
    
4. **Readable enemy behavior**
    
5. **Combat-aware level geometry**
    
6. **Exploration**
    
7. **World-building and presentation**
    
8. **Secondary systems**
    

The project should always be able to answer:

> **Does this make the sword fighting and exploration better?**

If a feature does not contribute to the intended experience, it should justify its existence before becoming part of the core game.

---

# 16. Current Scope Boundary

This document intentionally does **not** define detailed systems for:

- progression
    
- inventory
    
- equipment
    
- quests
    
- economy
    
- magic/spell systems
    
- bosses
    
- enemy roster
    
- story
    
- save systems
    

Those can be defined separately once their designs are established.

The purpose of this document is to establish the **identity and non-negotiable gameplay foundations** of Spellbreakers.