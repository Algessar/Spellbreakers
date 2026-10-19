[ai-ignore]

[this is just me trying to organise my thoughts]

Parrying is the clash of two guard transitions; meaning angles of attack. 
For example Zornhau/Zornhau has opposing cut angles with the blade angles crossing. So the first rule is`like parries like`. 
I can't figure out any other rules right now.

Maybe parrying could actually also be mainly behaviour? We have enums for Guards, meaning that every enemy has all of them available.
That might be better to change to Scriptable Objects, so that they can be chosen per enemy type? Then we perhaps can also add in
parrying clips in that SO for automatic matching? A list of GuardSO's that you add to the enemy type and it just adds the
name from that clip to the Animator Controller? The enum can still exist and then be set in the SO.
[THIS ALREADY SEEMS TO EXIST]

[Conversation]

DSH:

"Bottom line: your instinct is right that the guards are just poses and the direction carries the semantics — that's the
load-bearing design decision and it holds. What would actually need work is not the guard interpretation but: self-transitions,
variable pass chains, a side dimension, per-species parry relations, and separating "attack volume" from "parry surface".
None of those are large, and three of them are on the doc's radar already."

[draft 1 answer to DSH]

"The circling around part is not directly related and is an AI-behaviour thing really (agression behaviour). We can look 
at that later, but I  think the current Utility AI system can already handle it or be easily adapted.

We should also consider adding in an Avoidance system at this point. I've avoided (hah) that since I've wanted to focus on 
the sword stuff first, and been unsure of key-mapping. The enemy AI system is ScriptableObject-based where you can select available guards,
which should mean that adding Avoidance/Parry preference should be trivial yeah?
Another thought is to also be able to add AnimationClips to the Scriptables (or split things at this point, so the Archetypes.

I'm actually seeing a lot that I've missed. going through all the code has been quite daunting. There are two ScriptableObjects 
already, Fencing Archetype and Combat Rules. Combat rules can seemingly set Guards, Transitions, Parry Relations and Transition Parries.
This is apparently not wired yet. ^^

Some of the most important things to fix right now:
$3.2 Self-transitions -> doubling up on attacks is a common strategy (plus, beginners have a limited range of attacks they do, 
often Zornhau left/right and maybe Mittelhau. This is definitely relevant for authoring enemy difficulty properly).
$5 Chained animations (reason being a realisation that I hard-lined too much on only having static guard poses. Actual,
moving intermediate animations will be created and added)"

About $4: I'm unsure if I should make this mechanically relevant. It would make the system more realistic if you have to 
match incoming vs parry crosswise so to speak, but it could also end up making combat too complex. keeping track of timing 
windows and which guard to match with might already be more than enough for most players. Tbf, it *could* also be part of a difficulty setting.

[draft 2 answer to DSH]

[This sounds good? Actually looks like it caught the most important things.] 

That actually sounds correct, and it seems you caught the most important changes. 
Clarification of variable pass-chains to avoid misunderstanding: This is mostly for animation-correction clips. I have a 
tendency to get locked into restrictions, this time 'there should only be static combat poses". This is referenced in 
Animation Philosophy.md and Procedural Animation.md. This is turning out to be unfeasible and I will author actual moving 
animation clips to smooth out the transitions.

So handling animations will be FromGuard->TransitionGuard (currently only Langort)->ToGuard, changing to 
FromGuard->Correction Clip->TransitionGuard->CorrectionClip->ToGuard.

I'm actually seeing a lot that I've missed. Going through all the code has been quite daunting. There are two ScriptableObjects
already, Fencing Archetype (enemy AI) and Combat Rules (override asset). Combat rules can seemingly set Guards, 
Transitions, Parry Relations and Transition Parries.This is apparently not wired yet.
There is a list of Allowed Target Guards in the Fencing Archetype ScriptableObject, which is awesome. This should mean that
it is trivial to set and limit for each enemy type, which is very good.

Something we should consider is to add Avoidance as a defensive option. This would help solve the issue with non-humanoids
parrying, where you can choose only avoidance skills instead, but still have the option of giving a creature parrying (appendages that
are hard enough should ofcourse still be usable for parrying). 

If something is unclear still please do ask. If we're in agreement we can proceed with implementation!

