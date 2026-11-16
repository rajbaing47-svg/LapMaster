using UnityEngine;

// Per-layer camera draw distances. Put this on the player camera.
// FIX: LayerMask.NameToLayer returns -1 if a layer doesn't exist, which used to throw
// IndexOutOfRange and stop the script. Missing layers are now skipped.
public class LayerCullDistances : MonoBehaviour
{
    void Start()
    {
        var cam = GetComponent<Camera>();
        float[] d = new float[32];          // 0 = use the camera's far plane

        Set(d, "Crowd", 60f);               // spectators vanish past 60m
        Set(d, "Lamps", 100f);
        Set(d, "Props", 80f);
        Set(d, "Buildings", 200f);

        cam.layerCullDistances = d;
        cam.layerCullSpherical = true;      // distance is measured in a sphere, not a plane
    }

    static void Set(float[] d, string layerName, float dist)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) { Debug.LogWarning($"LayerCullDistances: layer '{layerName}' doesn't exist - skipped."); return; }
        d[layer] = dist;
    }
}