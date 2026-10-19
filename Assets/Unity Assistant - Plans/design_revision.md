ai-ignore
not finished, will change / moved into DEV/v0.2


## Revision / Clarification


First of all I must tell myself to stop putting dumb restrictions on this. I have been hardlining against actually 
animating the transitions between guards, but I will stop being an idiot about that. I will hand-author all transitions seen in
the table below.


* Note: The difference between Zornhau and Oberhau is, to me, speed, wind-up and distance. Zornhau is straight forward and snappy,
  while Oberhau is making a longer arc. (This can mean though that Zornhau generally ends in an upper guard to make it snappy and quick.
  Oberhau would then be a set-up for the next transition in a more fluid way, increasing the power of the next strike?)

### Issues:
- The current in-stance locomotion is too slow.
- Look At IK implementation and/or upper body animation is flawed
- 

## Stance

Currently, getting into stance is an active decision. This might not be suiting.

Consider: stance and target acquisition is activated when getting in range of an enemy player. The Stance button is changed to Targeting
and will be used to deliberately switch from current.


## Targeting

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

## Animation

**Guard-passes**

Langort will be kept as pass-only.

Since I only have one side (right) for guards right now, there can only be certain passes for now. Right/left version of guards is dependent on leading foot.
For right side guard, the leading foot is *left*.
(Note: mirroring guards is technically possible, but there are generally too many differences in pose for that, even though it might seem initially like it would work.)


VomTag -> Iron Gate would cut through a high langort against the target's right shoulder and pass down. 


| From     | To           | Through                       | Meisterhau               |
|----------|--------------|-------------------------------|--------------------------|
| VomTag   | VomTag       | Langort -> Alber -> Ochs      | Zornhau                  | <- new consideration**. only for VomTag?
| VomTag   | Pflug        | Langort                       | Thrust (Stichen)         |
| VomTag   | Alber        | Langort                       | Zornhau -> Step?         |
| VomTag   | Ochs         | Langort -> Alber -> Iron Gate | Zornhau                  |
| VomTag   | Iron Gate    | Langort                       | Zornhau                  |
|----------| ------------ | ------------------            | ------------------------ |
| Ochs     | VomTag       | High Langort (New?)           | Schwerchau               |
| Ochs     | Pflug        | Langort (high?)               | Stichen                  |
| Ochs     | Alber        | Langort                       | Oberhau                  |
| Ochs     | Iron Gate    | Langort -> Pflug              | Zornhau                  |
|----------| ------------ | ------------------            | ------------------------ |
| Alber    | VomTag       | Pflug/Langort*                | Unterhau   (false edge)  |
| Alber    | Pflug        | Langort                       | Stichen                  |
| Alber    | Ochs         | Langort                       | Unterhau                 |
| Alber    | Iron gate    | Pflug                         | Mittelhau                |
|----------| ------------ | ------------------            | ------------------------ |
| IronGate | VomTag       | Pflug/Langort*                | Unterhau                 |
| IronGate | Pflug        | Pflug/Langort*                | Stichen                  |
| IronGate | Alber        | Pflug/Langort*                | Mittelhau                |
| IronGate | Ochs         | Pflug/Langort*                | Unterhau                 |
|----------| ------------ | ------------------            | ------------------------ |
| Pflug    | Vom Tag      | Langort                       | Stichen                  |
| Pflug    | IronGate     | Langort                       | Stichen                  |
| Pflug    | Alber        | Langort                       | Stichen                  |
| Pflug    | Ochs         | Langort                       | Stichen                  |

* Depends on distance

** Consideration; certain guards allow triggering on itself:
  
    VomTag -> VomTag => basic Zornhau
  
    Pflug -> Pflug => basic Stichen
  
    Ochs -> Ochs => Swerchau

Alternatively, they would work as side-switches. So
    
    R VomTag -> L VomTag would ofc be Zornhau    
    R IronGate -> L IronGate => Mittelhau






