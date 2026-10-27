# 6. Current Transition Table

Guard Pose, Correction Pose and Transition Pose are the terms that will be used henceforth.

Guard Pose is the single pose definition the guard, such as VomTag
Transition Pose is the extreme extension that is passed through during a Hew
Correction Pose is an authered pass-through animation to smooth out the animation and ensure it looks good.

Transition pose is as of now only Langort.
Candidates:

Transition-Scwerhau (high langort, blade flat horizonally) to ensure it's a cut and not a thrust
Transition-Hengen (sword held angled above head, usually over shoulder)
Transition-Kron (possible triggered as parry?)


The following describes the intended current transition language.

| From      | To        | TransitionPose   | Meisterhau / Attack | CorrectionPose In  | CorrectionPose Out |     
| --------- | --------- |------------------|---------------------|--------------------|--------------------|    
| Vom Tag   | Vom Tag   | Langort          | Zornhau             | No                 | No                 |     
| Vom Tag   | Pflug     | Langort          | Stichen             | No?                | No?                |     
| Vom Tag   | Alber     | Langort          | Zornhau             |                    |                    |     
| Vom Tag   | Ochs      | Langort          | Zornhau             |                    |                    |     
| Vom Tag   | Iron Gate | Langort          | Zornhau             |                    |                    |     
| Ochs      | Vom Tag   | High Langort     | Schwerhau           |                    |                    |     
| Ochs      | Pflug     | Langort (high?)  | Stichen             |                    |                    |     
| Ochs      | Alber     | Langort          | Oberhau             |                    |                    |     
| Ochs      | Iron Gate | Langort / Pflug* | Zornhau             |                    |                    |     
| Alber     | Vom Tag   | Pflug / Langort* | Unterhau            |                    |                    |     
| Alber     | Pflug     | Langort          | Stichen             |                    |                    |     
| Alber     | Ochs      | Langort          | Unterhau            |                    |                    |     
| Alber     | Iron Gate | Pflug            | Mittelhau           |                    |                    |     
| Iron Gate | Vom Tag   | Pflug / Langort  | Unterhau            |                    |                    |     
| Iron Gate | Pflug     | Pflug / Langort  | Stichen             |                    |                    |     
| Iron Gate | Alber     | Pflug / Langort  | Mittelhau           |                    |                    |     
| Iron Gate | Ochs      | Pflug / Langort  | Unterhau            |                    |                    |     
| Pflug     | Vom Tag   | Langort          | Stichen             |                    |                    |     
| Pflug     | Iron Gate | Langort          | Stichen             |                    |                    |     
| Pflug     | Alber     | Langort          | Stichen             |                    |                    |     
| Pflug     | Ochs      | Langort          | Stichen             |                    |                    |     

`*` Some transitions depend on distance. [revision: transition selection depends in general on distance!]


This table is **not yet final**. It is a working combat-language definition and should be revised as the playable system reveals what actually feels correct.

[Specific correction poses to animate]

**To Langort**

- Corr-Ochs-Langort ✔
- Corr-VomTag-Langort ✔
- Corr-Alber-Langort ✔
- Corr-Pflug-Langort ✔
- Corr-IronGate-Langort ✔

**From Langort**

- Corr-Langort-VomTag 
- Corr-Langort-Pflug
- Corr-Langort-Alber
- Corr-Langort-Ochs
- Corr-Langort-IronGate

