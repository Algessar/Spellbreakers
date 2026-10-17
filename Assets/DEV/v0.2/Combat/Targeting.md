# 15. Targeting

The player uses target acquisition / lock-on when engaging opponents.

Targeting should provide:

- a clear opponent focus
    
- directional duelling steering
    
- appropriate combat positioning
    
- reliable reacquisition
    

### Current issues

The current implementation has several known problems:

- Manually un-targeting can make reacquisition difficult or impossible.
    
- Reacquisition may only become possible after the previous target dies.
    
- Pressing the stance/target button does not always acquire a target.
    
- With multiple targetable objects, target selection can sometimes choose an object that is farther away when a nearer target is expected.
    

These are implementation bugs, not intended design behavior.
