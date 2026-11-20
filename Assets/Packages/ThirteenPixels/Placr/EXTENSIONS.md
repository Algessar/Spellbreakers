# Placr — Spellbreakers extensions

Placr 1.1.2 by Sascha Graeff / 13Pixels, extended in-place for this project.
Everything from the original tool still works the same way; the additions are additive and can be
ignored by simply leaving "Use Prefab Set" and "Randomize Y Rotation" switched off.

---

## 1. What the original Placr does

* Adds a **Placr** tool to the Scene view toolbar (next to the move/rotate/scale tools).
* Activate the tool, then select a prefab (or 3D model) in the Project window: the next left clicks
  in the Scene view **place instances** at the cursor.
* **Left click** place · **Right click** stop · **Ctrl + drag** rotate · **Shift + drag** change vertical offset
  (**Shift + Right click** resets it).
* **Raycast modes** – `Ground Raycast` (hit point, upright), `Surface Raycast` (aligned to the hit normal),
  `Fixed Ground Plane` (y = 0).
* **Grid** – snap the position to a configurable offset/size per axis.
* **Angle Snap** – 0/15/45/90 presets or any angle; yaw is applied around the hit normal.
* **Pick up** – grab an already placed scene object and move it with the same controls.
* **Modifiers** (collapsible panel) – applied to every placed instance:
  `Random Rotational Offset`, `Random Tilt`, `Random Scale`.
* Multi-selection of several prefabs places a **random one of them per click**.

## 2. What this project added

### Prefab Set — place a whole list at once
`PlacrPrefabSet.cs`

* A reorderable **list of prefabs** in the Placr panel (`Prefab Set` foldout). Drag prefabs onto it,
  or use **Add Project Selection** to append everything that is selected in the Project window.
* One left click places **every entry of the list simultaneously**, spaced by the **Spacing** vector.
  The spacing follows the current placement rotation, so rotating with `Ctrl + drag` rotates the whole row.
* **Center On Cursor** – the set is centred around the cursor instead of starting at it.
* **Snap Each To Ground** – each element is raycast down onto its own ground point, so a row of props
  follows slopes and uneven terrain (disabled in `Fixed Ground Plane` mode).
* The whole set is a **single undo step**, and it respects the placement parent.
* The list, spacing and toggles are **persisted in the editor preferences**.
* While **Use Prefab Set** is on, clicks place the set instead of the Project selection.
  Right click still stops placing; press **Place Set** to arm it again.

### Randomize Y Rotation
`PlacrRandomYRotation.cs`

* A first-class toggle in the Placr panel (no need to open the Modifiers panel).
* **Yaw** min/max range slider (0–360°) plus an optional **Snap To** step, e.g. `90` for four-way aligned props.
* The random yaw is **visible in the preview** and is re-rolled after every placement,
  so consecutive clicks never repeat.
* Note: this stacks with the existing `Random Rotational Offset` modifier — use one of the two.

### Persistent placement parent
`PlacrParentSettings.cs`

* The original `New Instance Parent` field was a plain static, so it was lost on every domain reload.
  The parent is now stored as a `GlobalObjectId` in `EditorPrefs` and survives recompiles, restarts and
  scene changes (as long as the object still exists).
* `Use Selected Object As Parent` – one click to parent to the selected scene object.
* `Root` – clears the parent so instances go to the scene root again.
* Prefab assets are rejected as parents; a destroyed parent is forgotten automatically.
* Applies to both single placements and Prefab Sets.

### Other
* `Placr.cs` was re-encoded from Windows-1252 to UTF-8; no behaviour change.
* Prototype cleanup (`StripPrototypeComponents`) and parenting are shared by both placement operations.

## 3. Files

| File | Role |
| --- | --- |
| `Editor/Placr.cs` | original tool; gained `PlacePrefabSetOperation`, random-YAW preview, shared helpers, panel wiring |
| `Editor/PlacrPrefabSet.cs` | prefab set data, persistence, list GUI |
| `Editor/PlacrRandomYRotation.cs` | random yaw option, persistence, GUI |
| `Editor/PlacrParentSettings.cs` | persistent placement parent, GUI |

## 4. Upgrading Placr

If the asset is ever re-imported, these changes are lost. Keep this file and the three
`Placr*Settings/Set/RandomYRotation` scripts as the reference for re-applying them.
