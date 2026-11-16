using UnityEngine;

// Put this on the ROOT panel of your mobile controls Canvas (the parent RectTransform that
// holds all 6 buttons). It shrinks that panel's anchors to fit inside Screen.safeArea, which
// Android reports per-device - so on a phone with a notch, punch-hole camera, or curved edges,
// your buttons automatically pull in from the edge instead of being cut off or sitting under
// the notch. On phones with no notch, safeArea == full screen, so nothing changes.
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    private RectTransform rect;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        // Cheap check every frame - only recalculates if the safe area actually changed
        // (e.g. phone rotated), so this isn't doing real work most frames.
        if (Screen.safeArea != lastSafeArea) Apply();
    }

    private void Apply()
    {
        Rect safeArea = Screen.safeArea;
        lastSafeArea = safeArea;

        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
    }
}