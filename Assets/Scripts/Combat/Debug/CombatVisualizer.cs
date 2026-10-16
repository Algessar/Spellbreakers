using System.Collections.Generic;
using Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Makes an encounter legible in the Game view.
    ///
    /// <see cref="FencingDebugView"/> answers "what is the system thinking" as a text panel, but its gizmos
    /// only exist in the Scene view - and the things worth watching in a duel are spatial: which guard
    /// reaches where, whether the two fighters are inside measure, whether a blade is live, and who just
    /// parried whom. So this draws real objects instead of gizmos, which means it renders in play mode at
    /// full size, from the player's own camera.
    ///
    /// It reads the same <see cref="GuardProfile"/> bands, <see cref="CombatContact"/> results and
    /// <see cref="TargetingSystem"/> state the rules use, so what is on screen cannot drift from what the
    /// simulation is actually doing. Toggle with F2.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVisualizer : MonoBehaviour
    {
        private const int RingSegments = 64;
        private const int SortingOrder = 32759;

        /// <summary>World scale for labels: 0.006 makes a 320-unit canvas about 1.9 m wide.</summary>
        private const float LabelWorldScale = 0.006f;

        private const float RadiusEpsilon = 0.0005f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Color RingenColor = new Color(1f, 0.35f, 0.25f, 1f);
        private static readonly Color KriegColor = new Color(1f, 0.70f, 0.20f, 1f);
        private static readonly Color ZufechtenColor = new Color(0.35f, 0.85f, 0.45f, 1f);
        private static readonly Color OutOfRangeColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        private static readonly Color TargetColor = new Color(1f, 0.85f, 0.10f, 1f);
        private static readonly Color AcquisitionColor = new Color(0.55f, 0.75f, 1f, 1f);
        private static readonly Color ParryColor = new Color(1f, 0.22f, 0.18f, 1f);
        private static readonly Color BindColor = new Color(1f, 0.60f, 0.15f, 1f);
        private static readonly Color DeflectColor = new Color(0.40f, 0.85f, 1f, 1f);
        private static readonly Color HitColor = new Color(1f, 0.95f, 0.85f, 1f);

        [Header("Toggle")]
        [Tooltip("Key that shows and hides every visual.")]
        [SerializeField] private Key _toggleKey = Key.F2;

        [SerializeField] private bool _visible = true;

        [Header("Layers")]
        [SerializeField] private bool _showTargetMarker = true;
        [SerializeField] private bool _showMeasureRings = true;
        [SerializeField] private bool _showTargetingRange = true;
        [SerializeField] private bool _showCombatantLabels = true;
        [SerializeField] private bool _showCombatText = true;

        [Header("Target marker")]
        [SerializeField, Min(0.1f)] private float _markerHeight = 2.15f;
        [SerializeField, Min(0.01f)] private float _markerRadius = 0.10f;
        [SerializeField, Min(0.05f)] private float _markerGroundRingRadius = 0.55f;

        [Header("Rings")]
        [SerializeField, Min(0.001f)] private float _ringWidth = 0.02f;

        private readonly Dictionary<CombatHandler, System.Action<CombatContact>> _contactSubscriptions =
            new Dictionary<CombatHandler, System.Action<CombatContact>>();

        private readonly List<CombatHandler> _pruneBuffer = new List<CombatHandler>();
        private readonly List<WorldLabel> _combatantLabels = new List<WorldLabel>();

        private Material _lineMaterial;
        private Material _markerMaterial;

        private LineRenderer _ringenRing;
        private LineRenderer _kriegRing;
        private LineRenderer _zufechtenRing;
        private LineRenderer _acquisitionRing;
        private LineRenderer _markerGroundRing;
        private LineRenderer _markerStem;
        private Transform _markerSphere;

        private WorldLabel _ringenLabel;
        private WorldLabel _kriegLabel;
        private WorldLabel _zufechtenLabel;
        private WorldLabel _measureLabel;

        private TargetingSystem _targeting;
        private Transform _lastTarget;
        private float _nextPruneTime;

        // Ring vertices are only rebuilt when a radius actually moves, which it does whenever the guard
        // changes - so this is worth caching rather than pushing 64 vertices per ring every frame.
        private float _cachedRingenRadius = -1f;
        private float _cachedKriegRadius = -1f;
        private float _cachedZufechtenRadius = -1f;
        private float _cachedAcquisitionRadius = -1f;

        private int _lastContactFrame = -1;
        private CombatContact _lastContact;
        private bool _built;

        private void Start()
        {
            // This is the player's debug eye. TargetingSystem exists only on a player-controlled character,
            // so its absence is what tells an enemy carrying the same prefab to stay quiet - otherwise
            // every fighter would draw its own rings and duplicate every contact message.
            _targeting = GetComponent<TargetingSystem>() ?? GetComponentInChildren<TargetingSystem>(true);
            if (_targeting == null)
            {
                enabled = false;
                return;
            }

            Build();
            _built = true;
        }

        private void Build()
        {
            _lineMaterial = CreateUnlitMaterial(Color.white);
            _markerMaterial = CreateUnlitMaterial(TargetColor);

            _ringenRing = CreateRing("MeasureRing_Ringen", RingenColor, RingSegments, true, false);
            _kriegRing = CreateRing("MeasureRing_Krieg", KriegColor, RingSegments, true, false);
            _zufechtenRing = CreateRing("MeasureRing_Zufechten", ZufechtenColor, RingSegments, true, false);
            _acquisitionRing = CreateRing("TargetingRange", AcquisitionColor, RingSegments, true, false);

            // The marker follows a target rather than the player, so these two live in world space.
            _markerGroundRing = CreateRing("TargetGroundRing", TargetColor, RingSegments, true, true);
            _markerStem = CreateRing("TargetStem", TargetColor, 2, false, true);

            _markerSphere = CreateMarkerSphere();

            _ringenLabel = new WorldLabel(transform, 14f, "RINGEN");
            _kriegLabel = new WorldLabel(transform, 14f, "KRIEG");
            _zufechtenLabel = new WorldLabel(transform, 14f, "ZUFECHTEN");
            _measureLabel = new WorldLabel(transform, 20f, string.Empty);
        }

        private void OnDestroy()
        {
            foreach (var pair in _contactSubscriptions)
            {
                if (pair.Key != null)
                    pair.Key.ContactResolved -= pair.Value;
            }

            _contactSubscriptions.Clear();

            DestroyRuntime(_lineMaterial);
            DestroyRuntime(_markerMaterial);

            for (var i = 0; i < _combatantLabels.Count; i++)
                _combatantLabels[i].Dispose();

            _combatantLabels.Clear();

            _ringenLabel?.Dispose();
            _kriegLabel?.Dispose();
            _zufechtenLabel?.Dispose();
            _measureLabel?.Dispose();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && _toggleKey != Key.None && keyboard[_toggleKey].wasPressedThisFrame)
                _visible = !_visible;

            if (!_visible)
            {
                HideEverything();
                return;
            }

            if (_targeting == null)
                _targeting = GetComponent<TargetingSystem>() ?? GetComponentInChildren<TargetingSystem>(true);

            SyncContactSubscriptions();

            if (Time.time >= _nextPruneTime)
            {
                _nextPruneTime = Time.time + 2f;
                PruneContactSubscriptions();
            }

            UpdateTargetMarker();
            UpdateMeasureRings();
            UpdateCombatantLabels();
        }

        private void LateUpdate()
        {
            if (!_visible)
                return;

            var rotation = WorldTextPopup.AnchorRotation();

            _ringenLabel?.FaceCamera(rotation);
            _kriegLabel?.FaceCamera(rotation);
            _zufechtenLabel?.FaceCamera(rotation);
            _measureLabel?.FaceCamera(rotation);

            for (var i = 0; i < _combatantLabels.Count; i++)
                _combatantLabels[i].FaceCamera(rotation);
        }

        // --- Target marker and targeting range ------------------------------------

        private void UpdateTargetMarker()
        {
            var target = _targeting != null ? _targeting.CurrentTarget : null;

            ReportTargetChange(target);

            var showMarker = _showTargetMarker && target != null;

            if (_markerSphere != null)
            {
                _markerSphere.gameObject.SetActive(showMarker);

                if (showMarker)
                {
                    _markerSphere.localScale = Vector3.one * (_markerRadius * 2f);
                    _markerSphere.position = target.position + Vector3.up * _markerHeight;
                }
            }

            SetRingActive(_markerGroundRing, showMarker);
            SetRingActive(_markerStem, showMarker);

            if (showMarker)
            {
                var basePosition = target.position;

                SetWorldRing(_markerGroundRing, basePosition + Vector3.up * 0.02f, _markerGroundRingRadius);

                _markerStem.SetPosition(0, basePosition + Vector3.up * 0.04f);
                _markerStem.SetPosition(1, basePosition + Vector3.up * (_markerHeight - _markerRadius * 2f));
            }

            var showRange = _showTargetingRange && _targeting != null && _targeting.IsTargeting;
            SetRingActive(_acquisitionRing, showRange);

            if (showRange)
                SetLocalRing(_acquisitionRing, _targeting.AcquisitionRange, ref _cachedAcquisitionRadius);
        }

        /// <summary>Announces a target being picked up, dropped or swapped, which is otherwise invisible.</summary>
        private void ReportTargetChange(Transform target)
        {
            if (target == _lastTarget)
                return;

            var previous = _lastTarget;
            _lastTarget = target;

            // Nothing to say until the player has engaged targeting at least once.
            if (!_showCombatText || (previous == null && target == null))
                return;

            var anchor = ResolveAim(transform) + Vector3.up * 0.35f;

            if (target == null)
                WorldTextPopup.Spawn(anchor, "TARGET LOST", OutOfRangeColor, 16f, 1.4f, 0.6f);
            else
                WorldTextPopup.Spawn(anchor, "TARGET: " + target.name, AcquisitionColor, 16f, 1.4f, 0.6f);
        }

        // --- Measure bands ---------------------------------------------------------

        private void UpdateMeasureRings()
        {
            var combat = GetComponent<CombatHandler>() ?? GetComponentInChildren<CombatHandler>(true);
            var agent = combat != null ? combat.Agent : null;
            var target = _targeting != null ? _targeting.CurrentTarget : null;

            if (agent == null)
            {
                HideMeasureRings();
                return;
            }

            var reach = agent.TipReach;
            var show = _showMeasureRings;

            SetRingActive(_ringenRing, show);
            SetRingActive(_kriegRing, show);
            SetRingActive(_zufechtenRing, show);
            _ringenLabel.SetVisible(show);
            _kriegLabel.SetVisible(show);
            _zufechtenLabel.SetVisible(show);

            if (!show)
            {
                _measureLabel.SetVisible(false);
                return;
            }

            // The three thresholds are exactly the ones GuardProfile.Classify bands against, so these rings
            // are the rule drawn, rather than a second opinion about it.
            var ringRadius = reach * 0.55f;
            var kriegRadius = reach;
            var zufechtenRadius = reach * 2f;

            SetLocalRing(_ringenRing, ringRadius, ref _cachedRingenRadius);
            SetLocalRing(_kriegRing, kriegRadius, ref _cachedKriegRadius);
            SetLocalRing(_zufechtenRing, zufechtenRadius, ref _cachedZufechtenRadius);

            var origin = transform.position;
            _ringenLabel.MoveTo(origin + Vector3.forward * ringRadius, LabelWorldScale);
            _kriegLabel.MoveTo(origin + Vector3.forward * kriegRadius, LabelWorldScale);
            _zufechtenLabel.MoveTo(origin + Vector3.forward * zufechtenRadius, LabelWorldScale);

            UpdateMeasureReadout(agent, target);
        }

        private void UpdateMeasureReadout(FencingAgent agent, Transform target)
        {
            if (target == null)
            {
                _measureLabel.SetVisible(true);
                _measureLabel.Set("NO TARGET", OutOfRangeColor);
                _measureLabel.MoveTo(ResolveAim(transform) + Vector3.up * 0.7f, LabelWorldScale);
                return;
            }

            var distance = Vector3.Distance(transform.position, target.position);
            var measure = agent.ClassifyMeasure(distance);

            var suffix = string.Empty;
            if (_targeting != null && _targeting.IsTargeting && distance > _targeting.AcquisitionRange)
                suffix = "  OUT OF RANGE";

            _measureLabel.SetVisible(true);
            _measureLabel.Set($"{MeasureName(measure)}  {distance:0.00} m{suffix}", MeasureColor(measure));
            _measureLabel.MoveTo(
                Vector3.Lerp(transform.position, target.position, 0.5f) + Vector3.up * 0.6f,
                LabelWorldScale);
        }

        private void HideMeasureRings()
        {
            SetRingActive(_ringenRing, false);
            SetRingActive(_kriegRing, false);
            SetRingActive(_zufechtenRing, false);
            _ringenLabel.SetVisible(false);
            _kriegLabel.SetVisible(false);
            _zufechtenLabel.SetVisible(false);
            _measureLabel.SetVisible(false);
        }

        // --- Per-combatant state ---------------------------------------------------

        private void UpdateCombatantLabels()
        {
            if (!_showCombatantLabels)
            {
                for (var i = 0; i < _combatantLabels.Count; i++)
                    _combatantLabels[i].SetVisible(false);
                return;
            }

            var index = 0;

            foreach (var damageable in Damageable.All)
            {
                if (damageable == null || !damageable.IsAlive)
                    continue;

                var combat = CombatantRegistry.FindOwnerComponent<CombatHandler>(damageable);
                var agent = combat != null ? combat.Agent : null;
                if (agent == null)
                    continue;

                var label = RentCombatantLabel(index++);

                // Phase is what makes a transition readable at a glance, and the blade going live recolours
                // the whole line so a strike window is obvious without reading any numbers.
                var color = agent.IsInDamageWindow
                    ? ParryColor
                    : agent.IsInParryWindow
                        ? ZufechtenColor
                        : Color.white;

                var text = $"{GuardIds.ToDisplayName(agent.Current)}  {agent.Phase}";
                if (agent.IsTransitioning)
                    text += $"  {agent.Progress01:0.00}";

                label.SetVisible(true);
                label.Set(text, color);
                label.MoveTo(damageable.AimPosition + Vector3.up * 0.45f, LabelWorldScale * 0.85f);
            }

            for (var i = index; i < _combatantLabels.Count; i++)
                _combatantLabels[i].SetVisible(false);
        }

        private WorldLabel RentCombatantLabel(int index)
        {
            while (_combatantLabels.Count <= index)
                _combatantLabels.Add(new WorldLabel(transform, 16f, string.Empty));

            return _combatantLabels[index];
        }

        // --- Contact feedback ------------------------------------------------------

        private void SyncContactSubscriptions()
        {
            foreach (var damageable in Damageable.All)
            {
                if (damageable == null)
                    continue;

                var handler = CombatantRegistry.FindOwnerComponent<CombatHandler>(damageable);
                if (handler == null || _contactSubscriptions.ContainsKey(handler))
                    continue;

                var captured = handler;
                System.Action<CombatContact> callback = contact => OnContactResolved(captured, contact);
                _contactSubscriptions.Add(handler, callback);
                handler.ContactResolved += callback;
            }
        }

        private void PruneContactSubscriptions()
        {
            if (_contactSubscriptions.Count == 0)
                return;

            _pruneBuffer.Clear();
            foreach (var pair in _contactSubscriptions)
            {
                if (pair.Key == null)
                    _pruneBuffer.Add(pair.Key);
            }

            for (var i = 0; i < _pruneBuffer.Count; i++)
                _contactSubscriptions.Remove(_pruneBuffer[i]);
        }

        /// <summary>
        /// Both fighters are told about a contact, so the same event arrives twice in one frame. The first
        /// one wins, and the text is anchored on the defender - which is the side the outcome actually
        /// happened to: a parry is the defender's reward, and a hit is the defender's problem.
        /// </summary>
        private void OnContactResolved(CombatHandler source, CombatContact contact)
        {
            if (Time.frameCount == _lastContactFrame && IsSameContact(_lastContact, contact))
                return;

            _lastContactFrame = Time.frameCount;
            _lastContact = contact;

            if (!_showCombatText || !_visible)
                return;

            WorldTextPopup.Spawn(
                ResolveContactAnchor(source, contact),
                OutcomeLabel(contact.Outcome),
                OutcomeColor(contact.Outcome),
                22f,
                contact.Outcome == CombatOutcome.Parry ? 1.5f : 1.1f,
                0.85f);
        }

        private Vector3 ResolveContactAnchor(CombatHandler source, in CombatContact contact)
        {
            // Prefer the defender, identified by the guard the contact names. When both fighters are in the
            // same guard that is ambiguous, so the handler that raised the event is the fallback.
            if (contact.DefenderGuard != GuardId.None && contact.DefenderGuard != contact.AttackerGuard)
            {
                foreach (var damageable in Damageable.All)
                {
                    if (damageable == null)
                        continue;

                    var handler = CombatantRegistry.FindOwnerComponent<CombatHandler>(damageable);
                    if (handler != null && handler.CurrentGuard == contact.DefenderGuard)
                        return damageable.AimPosition + Vector3.up * 1.35f;
                }
            }

            return source != null
                ? ResolveAim(source.transform) + Vector3.up * 1.35f
                : transform.position + Vector3.up * 1.5f;
        }

        private static bool IsSameContact(in CombatContact a, in CombatContact b)
        {
            return a.Outcome == b.Outcome &&
                   a.AttackerGuard == b.AttackerGuard &&
                   a.DefenderGuard == b.DefenderGuard &&
                   Mathf.Abs(a.Progress01 - b.Progress01) < 0.001f;
        }

        private static string OutcomeLabel(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.Parry: return "PARRY";
                case CombatOutcome.Bind: return "BIND";
                case CombatOutcome.Deflect: return "DEFLECT";
                case CombatOutcome.HitBody: return "HIT";
                default: return outcome.ToString().ToUpperInvariant();
            }
        }

        private static Color OutcomeColor(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.Parry: return ParryColor;
                case CombatOutcome.Bind: return BindColor;
                case CombatOutcome.Deflect: return DeflectColor;
                case CombatOutcome.HitBody: return HitColor;
                default: return Color.white;
            }
        }

        // --- Measurement helpers ---------------------------------------------------

        private static string MeasureName(Measure measure)
        {
            switch (measure)
            {
                case Measure.Ringen: return "RINGEN";
                case Measure.Krieg: return "KRIEG";
                case Measure.Zufechten: return "ZUFECHTEN";
                default: return "OUT OF RANGE";
            }
        }

        private static Color MeasureColor(Measure measure)
        {
            switch (measure)
            {
                case Measure.Ringen: return RingenColor;
                case Measure.Krieg: return KriegColor;
                case Measure.Zufechten: return ZufechtenColor;
                default: return OutOfRangeColor;
            }
        }

        private static Vector3 ResolveAim(Transform subject)
        {
            if (subject == null)
                return Vector3.zero;

            var damageable = subject.GetComponentInChildren<Damageable>(true);
            return damageable != null ? damageable.AimPosition : subject.position + Vector3.up * 1.2f;
        }

        // --- Runtime object construction -------------------------------------------

        private LineRenderer CreateRing(string name, Color color, int segments, bool loop, bool worldSpace)
        {
            var ringObject = new GameObject(name);
            ringObject.transform.SetParent(transform, false);
            ringObject.SetActive(false);

            var line = ringObject.AddComponent<LineRenderer>();
            line.useWorldSpace = worldSpace;
            line.loop = loop;
            line.positionCount = segments;
            line.widthMultiplier = _ringWidth;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            if (_lineMaterial != null)
                line.material = _lineMaterial;

            return line;
        }

        /// <summary>Rebuilds a ring in local space, so it follows the character for free.</summary>
        private static void SetLocalRing(LineRenderer ring, float radius, ref float cachedRadius)
        {
            if (ring == null || Mathf.Abs(cachedRadius - radius) < RadiusEpsilon)
                return;

            cachedRadius = radius;

            for (var i = 0; i < RingSegments; i++)
            {
                var angle = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        /// <summary>Rebuilds a flat ring around a world position.</summary>
        private static void SetWorldRing(LineRenderer ring, Vector3 centre, float radius)
        {
            if (ring == null)
                return;

            for (var i = 0; i < RingSegments; i++)
            {
                var angle = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        private Transform CreateMarkerSphere()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "TargetMarker";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (_markerRadius * 2f);

            // The primitive arrives with a collider, which would show up in the blade's swept test and in
            // targeting. A debug marker must never be part of the simulation.
            var collider = sphere.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null && _markerMaterial != null)
                renderer.sharedMaterial = _markerMaterial;

            sphere.SetActive(false);
            return sphere.transform;
        }

        private void HideEverything()
        {
            if (_markerSphere != null)
                _markerSphere.gameObject.SetActive(false);

            SetRingActive(_markerGroundRing, false);
            SetRingActive(_markerStem, false);
            SetRingActive(_acquisitionRing, false);
            SetRingActive(_ringenRing, false);
            SetRingActive(_kriegRing, false);
            SetRingActive(_zufechtenRing, false);

            _ringenLabel.SetVisible(false);
            _kriegLabel.SetVisible(false);
            _zufechtenLabel.SetVisible(false);
            _measureLabel.SetVisible(false);

            for (var i = 0; i < _combatantLabels.Count; i++)
                _combatantLabels[i].SetVisible(false);
        }

        private static void SetRingActive(LineRenderer ring, bool active)
        {
            if (ring != null && ring.gameObject.activeSelf != active)
                ring.gameObject.SetActive(active);
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning("CombatVisualizer: no unlit shader found; visuals will use the default material.");
                return null;
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);
            else if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);

            return material;
        }

        private static void DestroyRuntime(Object target)
        {
            if (target != null)
                Destroy(target);
        }

        /// <summary>
        /// A world-space label that can be re-pointed and re-texted every frame. Popups are for one-off
        /// events; this is for state that has to stay readable for as long as it is true.
        /// </summary>
        private sealed class WorldLabel
        {
            private readonly GameObject _root;
            private readonly RectTransform _rect;
            private readonly TextMeshProUGUI _text;

            public WorldLabel(Transform parent, float fontSize, string initial)
            {
                _root = new GameObject("CombatLabel", typeof(RectTransform), typeof(Canvas));
                _root.transform.SetParent(parent, false);

                var canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = SortingOrder;
                canvas.worldCamera = Camera.main;

                _rect = (RectTransform)_root.transform;
                _rect.sizeDelta = new Vector2(320f, 48f);
                _rect.localScale = Vector3.one * LabelWorldScale;

                var labelObject = new GameObject("Text", typeof(RectTransform));
                labelObject.transform.SetParent(_root.transform, false);

                var labelRect = (RectTransform)labelObject.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                _text = labelObject.AddComponent<TextMeshProUGUI>();
                _text.fontSize = fontSize;
                _text.fontStyle = FontStyles.Bold;
                _text.alignment = TextAlignmentOptions.Center;
                _text.textWrappingMode = TextWrappingModes.NoWrap;
                _text.raycastTarget = false;
                _text.color = Color.white;
                _text.SetText(initial ?? string.Empty);
            }

            public void Set(string text, Color color)
            {
                if (_text == null)
                    return;

                _text.SetText(text);
                _text.color = color;
            }

            public void MoveTo(Vector3 worldPosition, float scale)
            {
                if (_root == null)
                    return;

                _root.transform.position = worldPosition;
                _rect.localScale = Vector3.one * scale;
            }

            public void FaceCamera(Quaternion rotation)
            {
                if (_root != null)
                    _root.transform.rotation = rotation;
            }

            public void SetVisible(bool visible)
            {
                if (_root != null && _root.activeSelf != visible)
                    _root.SetActive(visible);
            }

            public void Dispose()
            {
                if (_root != null)
                    Object.Destroy(_root);
            }
        }
    }
}
