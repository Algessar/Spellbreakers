[under revision]

The current implementation of combat is stable. FromGuard -> Transition -> ToGuard is working as it should. 

# Perfect Actions and Triggered Effects

Triggered effects can happen on:

Parry: parrying at a certain timing should produce a Perfect Parry
Bind: Bind should allow for a quick follow-up by reacting to it in time (similar to the mechanic in Wind-waker, where
you can let your shield down and wait for a strike. If the timing is hit, you get a special roll- or
jump attack, depending on stick direction).
Effect of bind->follow-up: [Undecided. Suggestions: Deflect+stagger combo? ]
Deflect: [Undecided. Suggestions: ]
Consequitive hits: [Undecided. Suggestions: Increased stagger effect per hit, damage increase simulating continuous momentum from sword 
rotation (each new hit does more damage than the last), ]

**Triggered Effects and Spirits - The Fantasy**

In addition to the physical advantages you get from good play, you can also trigger magical effects. See Spirits.md.


[under revision]
# Combat Flow and Combos

The game should not rely on traditional canned combo strings.

However, uninterrupted offensive flow is desirable.

A planned **uninterrupted combo bonus** may reward the player for maintaining successful transitions without losing offensive momentum.

The intent is:

```
Guard
  ↓
Attack
  ↓
New Guard
  ↓
Attack
  ↓
New Guard
  ↓
Attack
```

rather than:

```
Attack 1 → Attack 2 → Attack 3
```

The player is chaining **combat transitions**, not executing a predetermined animation sequence.