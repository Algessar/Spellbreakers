using UnityEngine;

/// <summary>Brief screen-space combat text positioned above a world target.</summary>
public sealed class FloatingCombatText : MonoBehaviour
{
    private Transform followTarget;
    private Vector3 worldPosition;
    private string message;
    private Color color;
    private float elapsed;
    private const float Lifetime = 1f;
    private GUIStyle style;

    public static void Show(Transform target, string text, Color textColor)
    {
        if (!target) return;
        FloatingCombatText floatingText = new GameObject("Floating Combat Text").AddComponent<FloatingCombatText>();
        floatingText.followTarget = target;
        floatingText.worldPosition = target.position;
        floatingText.message = text;
        floatingText.color = textColor;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= Lifetime) Destroy(gameObject);
        if (followTarget) worldPosition = followTarget.position;
    }

    private void OnGUI()
    {
        Camera viewCamera = Camera.main;
        if (!viewCamera) return;
        Vector3 screen = viewCamera.WorldToScreenPoint(worldPosition + Vector3.up * 2.2f);
        if (screen.z <= 0f) return;

        float rise = elapsed * 28f;
        Rect label = new Rect(screen.x - 50f, Screen.height - screen.y - rise, 100f, 28f);
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
        }

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(label.x + 1f, label.y + 1f, label.width, label.height), message, style);
        GUI.color = color;
        GUI.Label(label, message, style);
        GUI.color = previousColor;
    }
}
