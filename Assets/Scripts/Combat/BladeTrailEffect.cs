using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Provides diegetic visual telegraphing for guard transitions:
    /// 1. Emits a clean cutting ribbon trail along the blade exclusively during <see cref="PassPhase.Strike"/>.
    /// 2. Manages trail fading and activation automatically based on the owner's <see cref="CombatHandler"/> phase.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Blade))]
    public sealed class BladeTrailEffect : MonoBehaviour
    {
        [Tooltip("Material used for the cutting ribbon trail.")]
        [SerializeField] private Material _trailMaterial;

        [Tooltip("Lifetime of trail ribbon segments in seconds.")]
        [SerializeField, Range(0.02f, 0.35f)] private float _trailTime = 0.12f;

        [Tooltip("Width of the trail ribbon along the blade.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _trailWidth = 0.08f;

        [SerializeField] private CombatHandler _combatHandler;
        [SerializeField] private Blade _blade;

        private TrailRenderer _trailRenderer;

        private void Awake()
        {
            if (_blade == null)
                _blade = GetComponent<Blade>();

            if (_combatHandler == null)
                _combatHandler = GetComponentInParent<CombatHandler>() ?? transform.root.GetComponentInChildren<CombatHandler>(true);

            SetupTrailRenderer();
        }

        private void OnEnable()
        {
            if (_combatHandler != null)
                _combatHandler.PhaseChanged += OnPassPhaseChanged;
        }

        private void OnDisable()
        {
            if (_combatHandler != null)
                _combatHandler.PhaseChanged -= OnPassPhaseChanged;

            if (_trailRenderer != null)
                _trailRenderer.emitting = false;
        }

        private void SetupTrailRenderer()
        {
            var tip = _blade != null && _blade.Tip != null ? _blade.Tip : transform;
            _trailRenderer = tip.GetComponent<TrailRenderer>();
            if (_trailRenderer == null)
                _trailRenderer = tip.gameObject.AddComponent<TrailRenderer>();

            _trailRenderer.time = _trailTime;
            _trailRenderer.startWidth = _trailWidth;
            _trailRenderer.endWidth = 0f;
            _trailRenderer.minVertexDistance = 0.02f;
            _trailRenderer.autodestruct = false;
            _trailRenderer.emitting = false;

            if (_trailMaterial != null)
            {
                _trailRenderer.material = _trailMaterial;
            }
            else
            {
                var defaultMat = Resources.Load<Material>("M_BladeTrail");
                if (defaultMat != null)
                    _trailRenderer.material = defaultMat;
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.4f, 0.7f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            _trailRenderer.colorGradient = gradient;
        }

        public void Configure(Material trailMaterial)
        {
            _trailMaterial = trailMaterial;
            if (_trailRenderer != null && _trailMaterial != null)
                _trailRenderer.material = _trailMaterial;
        }

        private void OnPassPhaseChanged(PassPhase phase)
        {
            if (_trailRenderer == null)
                return;

            // Ribbon is strictly emitted during live cutting damage window
            bool isLiveStrike = phase == PassPhase.Strike;
            _trailRenderer.emitting = isLiveStrike;

            if (!isLiveStrike)
            {
                // Clear any remaining vertex history when exiting the strike
                _trailRenderer.Clear();
            }
        }
    }
}
