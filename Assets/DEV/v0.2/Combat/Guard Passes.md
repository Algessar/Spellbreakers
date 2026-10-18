# 5. Guard Passes

**Langort is retained as a pass-only guard.**

The combat motion may pass through Langort without necessarily ending there.

Examples:

```
Vom Tag
   ↓
Langort
   ↓
Alber
```

or:

```
Ochs
   ↓
Langort
   ↓
Pflug
```

In more general terms:

```
FromGuard
   ↓
CorrectionPose
   ↓
TransitionPose/Guard (pose might be a better term, since Langort is not a static Guard in that sense.)
   ↓
CorrectionPose
   ↓
ToGuard
```

The pass represents a meaningful intermediate combat position rather than requiring every attack to have a bespoke intermediate pose.

Some transitions may instead pass through other guards depending on the attack and distance.
