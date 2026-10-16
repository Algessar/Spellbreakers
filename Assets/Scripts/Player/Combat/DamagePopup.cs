using System.Collections;
using TMPro;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// A single floating damage number. Builds its own world-space canvas at runtime so no prefab or
    /// manual canvas setup is required, then floats up and fades out before destroying itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamagePopup : MonoBehaviour
    {
        private const int SortingOrder = 32760;
        private const float Lifetime = 1.1f;
        private const float RiseDistance = 1.15f;

        /// <summary>World scale applied to the canvas: 0.01 makes 1 canvas unit equal 1 cm.</summary>
        private const float CanvasWorldScale = 0.01f;

        /// <summary>Canvas size in canvas units (so 120 x 40 is 1.2 m x 0.4 m in world space).</summary>
        private static readonly Vector2 CanvasSize = new Vector2(120f, 40f);

        private static readonly Color DamageColor = new Color(1f, 0.45f, 0.15f, 1f);

        /// <summary>Temporary: set by WeaponDamage when its own hit logging is enabled.</summary>
        public static bool VerboseLogging;

        private TextMeshProUGUI _text;
        private Transform _anchor;
        private Vector3 _offset;
        private Vector3 _drift;
        private float _elapsed;

        /// <summary>Creates a popup that drifts upward from <paramref name="anchor"/>.</summary>
        public static void Spawn(Transform anchor, float amount)
        {
            // No CanvasScaler: a world-space canvas must scale by transform, not by screen size.
            var popupObject = new GameObject(
                "DamagePopup",
                typeof(RectTransform),
                typeof(Canvas));

            popupObject.AddComponent<DamagePopup>().Build(anchor, amount);
        }

        private void Build(Transform anchor, float amount)
        {
            _anchor = anchor;
            _offset = ResolveSpawnOffset(anchor);
            _drift = new Vector3(Random.Range(-0.35f, 0.35f), 0f, 0f);

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            canvas.worldCamera = Camera.main;

            var canvasRect = (RectTransform)transform;
            canvasRect.sizeDelta = CanvasSize;
            canvasRect.localScale = Vector3.one * CanvasWorldScale;
            canvasRect.position = ResolveAnchorPosition() + _offset;
            canvasRect.rotation = ResolveBillboardRotation();

            CreateLabel();
            _text.SetText("{0}", Mathf.RoundToInt(amount));
            LogVisibility();
            StartCoroutine(Animate());
        }

        private void CreateLabel()
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _text = labelObject.AddComponent<TextMeshProUGUI>();
            // Canvas is 40 units tall at 0.01 world scale, so font 16 is roughly 16 cm of text.
            _text.fontSize = 16f;
            _text.fontStyle = FontStyles.Bold;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.raycastTarget = false;
            _text.color = DamageColor;

            // TMP Essential Resources must be imported for the default font to resolve.
            if (_text.font == null)
            {
                Debug.LogError(
                    "DamagePopup: TextMeshPro has no default font. Run Window > TextMeshPro > " +
                    "Import TMP Essential Resources.", this);
            }
        }

        /// <summary>
        /// Temporary diagnostic: proves whether the popup landed inside the view frustum, which is the
        /// one thing that cannot be confirmed from code inspection alone.
        /// </summary>
        private void LogVisibility()
        {
            if (!VerboseLogging)
                return;

            var viewCamera = Camera.main;
            var position = transform.position;

            if (viewCamera == null)
            {
                Debug.LogWarning($"[Popup] spawned at {position} but Camera.main is NULL - nothing can billboard.", this);
                return;
            }

            var viewport = viewCamera.WorldToViewportPoint(position);
            var onScreen = viewport.z > 0f &&
                           viewport.x >= 0f && viewport.x <= 1f &&
                           viewport.y >= 0f && viewport.y <= 1f;

            Debug.Log(
                $"[Popup] '{_text.text}' at {position} scale={transform.localScale.x:F4} " +
                $"canvas={((RectTransform)transform).sizeDelta} worldSize={((RectTransform)transform).sizeDelta * CanvasWorldScale} " +
                $"viewport=({viewport.x:F2},{viewport.y:F2},{viewport.z:F2}) " +
                $"distance={Vector3.Distance(viewCamera.transform.position, position):F1} " +
                $"onScreen={onScreen} fontSize={_text.fontSize} alpha={_text.color.a:F2} " +
                $"font={(_text.font == null ? "NULL" : _text.font.name)}", this);
        }

        /// <summary>Offset from the anchor's pivot to just above the top of its rendered bounds.</summary>
        private static Vector3 ResolveSpawnOffset(Transform anchor)
        {
            var rise = Vector3.up * 1.1f;
            if (anchor == null)
                return rise;

            var renderers = anchor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return rise;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return new Vector3(bounds.center.x - anchor.position.x, bounds.max.y - anchor.position.y + 0.25f,
                bounds.center.z - anchor.position.z);
        }

        private Vector3 ResolveAnchorPosition()
        {
            return _anchor ? _anchor.position : Vector3.zero;
        }

        private static Quaternion ResolveBillboardRotation()
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

        private IEnumerator Animate()
        {
            while (_elapsed < Lifetime)
            {
                _elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(_elapsed / Lifetime);

                // Rise quickly then ease out, with a little sideways spread.
                var rise = RiseDistance * (1f - Mathf.Pow(1f - progress, 2f));
                transform.position = ResolveAnchorPosition() + _offset + Vector3.up * rise + _drift * (1f - progress);
                transform.localScale = Vector3.one * (CanvasWorldScale * Mathf.Lerp(1.15f, 0.85f, progress));

                if (_text)
                {
                    var color = _text.color;
                    // Hold full opacity for the first half, then fade out.
                    color.a = Mathf.Clamp01((1f - progress) * 2f);
                    _text.color = color;
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            transform.rotation = ResolveBillboardRotation();
        }
    }
}
