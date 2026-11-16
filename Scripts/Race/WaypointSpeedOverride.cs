using UnityEngine;

// Attach this to individual waypoint node GameObjects (the ones tagged with InputManager's
// waypointTag, e.g. "Waypoints") to force AI cars to a specific speed while THIS node is their
// current target - overriding the normal automatic corner-speed detection for just that node.
public class WaypointSpeedOverride : MonoBehaviour
{
    [Tooltip("Target speed (KPH) AI cars will hold while heading toward THIS waypoint. Still multiplied by InputManager's Speed Multiplier, so your global difficulty knob keeps working even on overridden nodes.")]
    public float overrideSpeedKph = 40f;
}