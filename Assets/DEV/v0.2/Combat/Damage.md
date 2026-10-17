[under revision]

# 12. Damage

Damage should eventually reflect the physical character of the attack rather than being entirely determined by a static attack ID.

Planned factors include:

- weapon velocity
    
- distance travelled
    
- impact conditions
    
- potentially other physical properties as the system develops
    

The initial implementation should remain simple enough to validate the underlying combat.

The important distinction is:

> **A sword should deal damage because an attack successfully impacts a target, not because the player happens to be in an attack animation.**

A single transition should not repeatedly apply damage every frame while the weapon overlaps a target.
