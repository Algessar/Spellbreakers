using TMPro;
using UnityEngine;

namespace Spellbreakers.Combat
{
    /// <summary>
    /// One short-lived piece of world-space combat text: "PARRY", "BIND", "TARGET LOST".
    ///
    /// Built the same way as the damage numbers - a world-space canvas assembled at runtime, so nothing
    /// has to be authored as a prefab - but kept as its own type so combat feedback and damage numbers
    /// can be tuned apart. Billboards every frame so it stays readable from wherever the camera is.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldTextPopup : MonoBehaviour
    {
        private const int SortingOrder = 32761;

        /// <summary>World scale applied to the canvas: 0.01 makes 1 canvas unit equal 1 cm.</summary>
        private const float CanvasWorldScale = 0.01f;

        private static readonly Vector2 CanvasSize = new Vector2(320f, 48f);

        private TextMeshProUGUI _text;
        private Vector3 _origin;
        private Vector3 _drift;
        private Color _color = Color.white;
        private float _lifetime = 1.2f;
        private float _riseDistance = 0.9f;
        private float _fontSize = 18f;
        private float _elapsed;
        private bool _built;

        /// <summary>Spawns text that drifts upward from <paramref name="worldPosition"/> and fades out.</summary>
        public static WorldTextPopup Spawn(
            Vector3 worldPosition,
            string message,
            Color color,
            float fontSize = 18f,
            float lifetime = 1.2f,
            float rise = 0.9f)
        {
            if (string.IsNullOrEmpty(message))
                return null;

            var popupObject = new GameObject(
                "CombatText",
                typeof(RectTransform),
                typeof(Canvas));

            var popup = popupObject.AddComponent<WorldTextPopup>();
            popup.Build(worldPosition, message, color, fontSize, lifetime, rise);
            return popup;
        }

        private void Build(Vector3 worldPosition, string message, Color color, float fontSize, float lifetime,
            float rise)
        {
            _origin = worldPosition;
            _color = color;
            _fontSize = fontSize;
            _lifetime = Mathf.Max(0.05f, lifetime);
            _riseDistance = rise;
            _drift = new Vector3(Random.Range(-0.25f, 0.25f), 0f, 0f);

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            canvas.worldCamera = Camera.main;

            var canvasRect = (RectTransform)transform;
            canvasRect.sizeDelta = CanvasSize;
            canvasRect.localScale = Vector3.one * CanvasWorldScale;
            canvasRect.position = _origin;

            CreateLabel(message);
            _built = true;
        }

        private void CreateLabel(string message)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _text = labelObject.AddComponent<TextMeshProUGUI>();
            _text.fontSize = _fontSize;
            _text.fontStyle = FontStyles.Bold;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.raycastTarget = false;
            _text.color = _color;
            _text.SetText(message);

            // TMP Essential Resources must be imported for the default font to resolve.
            if (_text.font == null)
            {
                Debug.LogError(
                    "WorldTextPopup: TextMeshPro has no default font. Run Window > TextMeshPro > " +
                    "Import TMP Essential Resources.", this);
            }
        }

        private void Update()
        {
            if (!_built)
                return;

            _elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(_elapsed / _lifetime);

            // Rise quickly then ease out, with a little sideways spread.
            var rise = _riseDistance * (1f - Mathf.Pow(1f - progress, 2f));
            transform.position = _origin + Vector3.up * rise + _drift * (1f - progress);
            transform.localScale = Vector3.one * (CanvasWorldScale * Mathf.Lerp(1.2f, 0.85f, progress));

            if (_text != null)
            {
                var color = _color;
                // Hold full opacity for the first half, then fade out.
                color.a = Mathf.Clamp01((1f - progress) * 2f) * _color.a;
                _text.color = color;
            }

            if (_elapsed >= _lifetime)
                Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (_built)
                transform.rotation = AnchorRotation();
        }

        internal static Quaternion AnchorRotation()
        {
            var viewCamera = Camera.main;
            if (!viewCamera)
                return Quaternion.identity;

            var forward = viewCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.up;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
