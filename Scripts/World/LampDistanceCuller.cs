using UnityEngine;

public class LampDistanceCuller : MonoBehaviour
{
    public Transform player;
    public Light[] lamps;            // fill from TrackLightingController's lamp list
    public float onDistance = 60f;

    float next;
    void Update()
    {
        if (Time.time < next || player == null) return;
        next = Time.time + 0.25f;    // 4x per second is enough

        float sqr = onDistance * onDistance;
        foreach (var l in lamps)
            if (l != null)
                l.enabled = (l.transform.position - player.position).sqrMagnitude < sqr;
    }
}