Enemy Combat

Enemies should participate in the same fundamental combat language as the player.

Enemies should be understandable through their physical actions.

The player should be able to read:

- enemy guard
    
- attack preparation
    
- attack direction
    
- commitment
    
- recovery
    
- openings
    

Enemy difficulty should primarily come from:

- timing
    
- positioning
    
- decision-making
    
- readable but meaningful behavior
    

rather than inflated health pools or damage values.

### Considerations for non-humanoid enemies:

The underlying combat system should not require enemies to be humanoid or to use the player's exact guard system.

The important abstraction is the **physical interaction**:

- attack direction and trajectory
- timing and active attack window
- attack reach and contact volume
- momentum / relative velocity
- defensive position and weapon/body placement
- impact, deflection and parry
- recovery and resulting openings

A humanoid enemy may use recognizable guards and guard transitions, while a non-humanoid enemy can use its own physical 
states and attack patterns. The enemy does not need to behave like a swordsman for the same combat interaction system to apply.

For example:

- A troll with a club can use a restricted set of guards and transitions with different animations, reach and timing.
- A wolf or similar creature can use bites, claw swipes and pounces; these can still be understood through their direction, timing and physical motion.
- A dragon can use claw swipes, tail sweeps, bites and other attacks. Some may correspond naturally to directional melee attacks, while others, such as breath attacks, may require entirely different defensive responses.
- Corrupted plants could use tendril lashes, thrusting attacks, overhead strikes or area attacks without needing to adopt humanoid guards.

Parrying and deflection should ideally operate on the physical relationship between the incoming attack and the defender 
rather than on the attacker's anatomy. A successful interaction may therefore exist on a gradient rather than being simply 
"parried" or "not parried":

**good timing / angle → clean deflection → partial deflection → knockback / stagger → failed defence / hit**

Mass, momentum, attack speed, contact angle and weapon/body geometry can influence the result. A sword might cleanly 
redirect a human sword strike, partially deflect a troll's club, or be overwhelmed by a massive dragon attack.

The player's **guard-transition system remains specialized to the player's longsword combat**, while the underlying attack, 
contact, damage and deflection systems should ideally be general enough to support very different enemy anatomies and attack styles.

The goal is not to translate every monster attack into a Lichtenauer technique. The goal is for different creatures to
participate in the same **physical combat language** while expressing it through their own bodies and behaviors.