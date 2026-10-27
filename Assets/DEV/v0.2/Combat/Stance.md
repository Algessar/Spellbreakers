# Stance

The original design treated entering combat stance as an explicit player decision.

This is currently being reconsidered.

A possible model is:

```
Free Locomotion
      ↓
Enemy enters relevant range
      ↓
Combat stance / target acquisition becomes active
      ↓
Stance button becomes a guard-selection / target-control input
```

The goal is to avoid making combat feel like the player must first activate a separate "sword fighting mode."

This remains a playtest-driven design decision.

**Status:** the model above is now implemented; see `Combat State.md` for the rule as it currently runs
and for what is still open.
