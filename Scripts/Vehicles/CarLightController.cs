using UnityEngine;

// Put this on the car's ROOT GameObject (same object controller.cs is on).
// Handles 3 kinds of car lights on ONE shared prefab, used by every car (player + AI):
//
//   1. Headlights - auto ON for Evening/Night, OFF for Day. Checked once on Start()
//      (time of day doesn't change mid-race, it's picked in the Garage beforehand).
//   2. Brake lights - ON whenever the car is under real braking (controller.IsBraking)
//      OR handbraking (controller.IsHandbraking). Works at ANY time of day.
//   3. Reverse lights - ON whenever controller.reverse is true. Works at ANY time of day.
//
// Headlights/brake/reverse all update every frame in Update() except headlights, which
// only need to be set once - but re-checking is cheap and harmless, so it's fine to leave
// them in Update() too in case you ever want a live time-of-day change to reflect
// immediately (e.g. a future "day/night cycle" feature).
[RequireComponent(typeof(controller))]
public class CarLightsController : MonoBehaviour
{
    [Header("Headlights (Evening/Night only)")]
    public Light leftHeadlight;
    public Light rightHeadlight;

    [Header("Brake Lights (any time - braking or handbrake)")]
    public Light[] brakeLights;

    [Header("Reverse Lights (any time - reverse gear)")]
    public Light[] reverseLights;

    private controller car;

    private void Awake()
    {
        car = GetComponent<controller>();
    }

    private void Start()
    {
        ApplyHeadlights();
    }

    private void Update()
    {
        // Brake lights: real braking OR handbrake, either counts as "braking" visually.
        bool brakingNow = car.IsBraking || car.IsHandbraking;
        SetLights(brakeLights, brakingNow);

        // Reverse lights: mirrors the car's own reverse gear state directly.
        SetLights(reverseLights, car.reverse);
    }

    private void ApplyHeadlights()
    {
        bool headlightsOn = RaceSettings.SelectedTimeOfDay == TimeOfDay.Evening
                          || RaceSettings.SelectedTimeOfDay == TimeOfDay.Night;

        if (leftHeadlight != null) leftHeadlight.enabled = headlightsOn;
        if (rightHeadlight != null) rightHeadlight.enabled = headlightsOn;
    }

    private void SetLights(Light[] lights, bool on)
    {
        if (lights == null) return;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null) lights[i].enabled = on;
        }
    }
}