using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// Marks the actual blade: the two points the swept hit test travels between.
    ///
    /// Making the blade an explicit thing removes the previous guesswork, where the tip was reverse
    /// engineered from whichever axis of a box collider happened to be longest, and lets both the hit
    /// test and the aim IK agree on exactly where the point of the sword is.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Blade : MonoBehaviour
    {
        [Tooltip("Where the blade starts - the grip, or the weapon bone.")]
        [SerializeField] private Transform _base;

        [Tooltip("The point of the blade. A child named 'Sword Tip' is found automatically.")]
        [SerializeField] private Transform _tip;

        [Tooltip("Radius used for the swept capsule test.")]
        [SerializeField, Min(0.005f)] private float _radius = 0.06f;

        public Transform Base => _base != null ? _base : transform;

        public Transform Tip => _tip;

        public float Radius => _radius;

        public bool IsValid => Tip != null && Base != null;

        public Vector3 BasePosition => Base.position;

        public Vector3 TipPosition => Tip != null ? Tip.position : transform.position;

        public float Length => Tip == null ? 0f : Vector3.Distance(BasePosition, TipPosition);

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// Resolves the tip and base if they were left unset. Kept public so editor tooling and the
        /// enemy prefab builder can run the same resolution code paths the game does.
        /// </summary>
        public void ResolveReferences()
        {
            if (_tip == null)
                _tip = FindDescendant(transform, "Sword Tip");

            if (_tip == null && transform.parent != null)
                _tip = FindDescendant(transform.root, "Sword Tip");

            if (_base == null)
                _base = transform;
        }

        public void Configure(Transform bladeBase, Transform tip, float radius = -1f)
        {
            _base = bladeBase;
            _tip = tip;

            if (radius > 0f)
                _radius = radius;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
                return null;

            if (root.name == name)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDescendant(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (Tip == null)
                return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawLine(BasePosition, TipPosition);
            Gizmos.DrawWireSphere(TipPosition, _radius);
        }
    }
}
