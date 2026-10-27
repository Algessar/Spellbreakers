# 15. Targeting

The player uses target acquisition / lock-on when engaging opponents.

Targeting should provide:

- a clear opponent focus
    
- directional duelling steering
    
- appropriate combat positioning
    
- reliable reacquisition
    

### Current issues

The current implementation has several known problems:

~~- Pressing the stance/target button does not always acquire a target.~~ 
This may have been caused by acquisition being relative to camera, with FreeLook cam Orbits set too far away/too high.
Setting lower values both made combat feel better and increased targeting accuracy.

- Manually un-targeting can make reacquisition difficult or impossible.
    
- Reacquisition may only become possible after the previous target dies.    
    
- With multiple targetable objects, target selection can sometimes choose an object that is farther away when a nearer target is expected.
    

These are implementation bugs, not intended design behavior.

### Related

Target acquisition now only exists while the player is in combat; see `Combat State.md`. Pressing target
out of combat asks to engage when a hostile is already close, and otherwise does nothing.
