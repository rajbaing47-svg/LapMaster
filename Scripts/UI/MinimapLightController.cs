using UnityEngine;

// Put this on the SAME GameObject as your minimap Camera (the one with MinimapFollowOrtho).
// It keeps the minimap always fully lit AND always the same background, regardless of
// TrackLightingController's day/evening/night changes on the real track.
//
// SETUP: just add this component to the Minimap Camera GameObject. Nothing else needed -
// it reads whatever layers that camera is ALREADY set to render (its own Culling Mask,
// whatever you configured to isolate the minimap's own content) and:
//   1. Creates its own directional light restricted to those same layers only, so this
//      light can never leak onto the real track/main camera's view.
//   2. Forces that light's intensity/color to a fixed value, ignoring TrackLightingController
//      entirely (that script only ever touches lights registered in ITS OWN streetLamps/
//      studLights lists + the scene's main "sun" - it never touches this one).
//   3. (New) Optionally forces the minimap camera to clear to a flat solid color instead of
//      the track's day/evening/night skybox, so the minimap's background doesn't darken either.
[RequireComponent(typeof(Camera))]
public class MinimapLightController : MonoBehaviour
{
    [Tooltip("Constant brightness for the minimap, independent of day/night. Set high enough to fully override even Night's near-zero sun + low ambient.")]
    public float minimapLightIntensity = 1.5f;
    public Color minimapLightColor = Color.white;

    [Header("Background")]
    [Tooltip("If true, the minimap camera always clears to minimapBackgroundColor instead of the track's current skybox - so the backdrop behind the track doesn't go dark at night either.")]
    public bool overrideBackgroundColor = true;
    public Color minimapBackgroundColor = new Color(0.12f, 0.12f, 0.12f);

    private Light minimapLight;
    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Auto-create a directional light as a child so it always follows the minimap
        // camera and never needs manual repositioning.
        GameObject lightObj = new GameObject("MinimapLight");
        lightObj.transform.SetParent(transform, false);
        lightObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // straight down

        minimapLight = lightObj.AddComponent<Light>();
        minimapLight.type = LightType.Directional;
        // Matches whatever the minimap camera is already isolated to render - no separate
        // "Minimap" layer needs to be created or assigned by hand anymore.
        minimapLight.cullingMask = cam.cullingMask;
        minimapLight.intensity = minimapLightIntensity;
        minimapLight.color = minimapLightColor;
        minimapLight.shadows = LightShadows.None;       // minimap doesn't need shadows

        if (overrideBackgroundColor)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = minimapBackgroundColor;
        }
    }
}