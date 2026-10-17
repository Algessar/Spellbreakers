# Animation Philosophy

The previous design placed too much importance on avoiding authored transition animations.

That restriction is removed completely.

The correct principle is:

> **Combat logic determines what transition occurs. Animation determines how that transition looks.**

A transition may therefore be represented by an authored animation if that produces a better result.

The combat system should remain independent of whether the visual realization is:

```
Guard A
   ↓
authored animation
   ↓
Guard B
```

or:

```
Guard A
   ↓
procedural interpolation
   ↓
Guard B
```

or:

```
Guard A
   ↓
authored motion
   +
procedural correction
   ↓
Guard B
```

The combat state and transition remain authoritative.

The animation system must not become the source of combat logic.
