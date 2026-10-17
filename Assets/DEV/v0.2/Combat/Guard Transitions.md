# 3. Guard Transitions

## 3.1 Complete Transition Space

Any guard should be able to transition to any other guard.

The transition table describes **how a particular transition is expressed**, not which transitions are mechanically permitted.

For example:

```
Vom Tag → Pflug
```

is one valid transition.

```
Vom Tag → Ochs
```

is another.

```
Vom Tag → Vom Tag
```

may also be valid as a self-transition.

The combat system should not artificially restrict transitions simply because some pairings are less optimal.

The difference between transitions is primarily:

- attack type
    
- line
    
- distance
    
- timing
    
- resulting position
    
- tactical usefulness
    

---

## 3.2 Self-Transitions

Certain guards may support attacks that begin and end in the same guard.

Current candidates:

```
Vom Tag → Vom Tag
    = Zornhau

Pflug → Pflug
    = Stichen

Ochs → Ochs
    = Swerchau
```

These may alternatively be represented as side-switches:

```
R Vom Tag → L Vom Tag
    = Zornhau

R Iron Gate → L Iron Gate
    = Mittelhau
```

This is an open design decision.

The important requirement is that the combat system must support the concept rather than assuming:

> destination guard must always differ from source guard.
