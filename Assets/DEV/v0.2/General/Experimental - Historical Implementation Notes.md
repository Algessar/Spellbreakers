# 31. Experimental / Historical Implementation Notes

The project previously experimented with a procedural transition system intended to avoid authoring individual attack transitions.

That system explored:

- four directional pass poses
    
- three-stage pose interpolation
    
- non-linear timing curves
    
- procedural weapon arcs
    
- procedural spine/torso dynamics
    
- procedural blade telegraphing
    

The conceptual model was:

```
From Guard
    ↓
Directional Pass
    ↓
To Guard
```

with separate pass poses for:

```
Oberhau
Mittelhau
Unterhau
Thrust
```

The system also divided transitions into:

```
ToPass
   ↓
Strike
   ↓
Settle
```

This remains useful terminology and may still be used by the combat system.

However:

> **The procedural implementation is not a design requirement.**

If authored transition animations provide better quality, responsiveness, or readability, they are the preferred solution.

The purpose of the combat architecture is to represent the **guard transition**, not to enforce a particular animation-generation technique.
