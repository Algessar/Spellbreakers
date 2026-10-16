ai-ignore
not finished, will change


## Revision / Clarification

### Stance

Currently, getting into stance is an active decision. This might not be suiting.

Consider: stance is activated when getting in range of an enemy player.

Consider: active choice to do it earlier?

Issues:
- The current in-stance locomotion is too slow.
- Look At IK implementation and/or upper body animation is flawed
- 

### Targeting

Bugs:
- Finding a target and then manually un-targeting makes it hard/impossible to re-acquire any target. It can be re-acquired if previous target dies.
- Pressing the stance/target button does not always acquire the target.
- With more than two objects that can be targeted, it sometimes selects the object furthest away.
- 

### Freeform text:

What I want is for the player to pass through guards as attack-selection. That is the important part. 
I would have *liked* to only have authored poses with procedural corrections inbetween, but it's starting
to seem like that is more trouble than it is worth. Scales are tipping toward actually making some transition animations.

Current feeling of the gameplay is ... unpolished, obviously. But with the targeting bug (not always going into stance and I'm
not sure it even gets a target sometimes) and the massive diff in speed between running and stance-walk it feels very clunky. 
It is, undoubtedly, better currently to *not* go into stance at all and just wail at the target.
The more I think about it, the easier things will probably be by just making the transition animations. 

I have not confirmed this, but it seems that the guard transition system has ended up limited - some guards can only 
transition to some other specific guard(s). Which is not correct. You can go from any guard to any other, it's
just a question what kind of cut it produces and optimal choice. 

Some other things I want is:
- velocity/distance based damage calculation.
- stagger for perfect parry
- uninterrupted combo bonus
