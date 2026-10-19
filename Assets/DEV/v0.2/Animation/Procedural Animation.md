[under review]

# Procedural Animation

Procedural animation remains an available tool, but is **not a requirement**.

Previous experimentation explored:

- three-point pose blending
    
- directional apex poses
    
- non-linear timing curves
    
- procedural cutting arcs
    
- procedural spine/torso movement
    
- weapon rotation corrections
    
- procedural telegraphing
    

The experimental implementation used four directional pass poses:

- `Pass_Oberhau`
    
- `Pass_Mittelhau`
    
- `Pass_Unterhau`
    
- `Pass_Thrust`
    

and attempted to interpolate:

```
From Guard
    ↓
Directional Pass
    ↓
To Guard
```

This approach is retained as a useful reference and possible future technique, but it is **not a design requirement**.

If hand-authoring a transition produces a better result with less complexity, the authored animation should be used.
