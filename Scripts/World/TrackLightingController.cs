using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Put ONE of these in each track scene (e.g. on an empty "LightingRig" GameObject).
// On Start() it reads RaceSettings.SelectedTimeOfDay (set by the player in the Showroom/
// Garage scene) and applies the matching skybox + sun settings, and switches all
// registered street lamps on or off.
//
// Street lamps: your spline instantiate script spawns the lamp prefabs along the track.
// Either:
//   (a) drag the spawned lamps' Light components into streetLamps in the Inspector once
//       spawning is done (fine if spawning happens in the Editor, not at runtime), OR
//   (b) if lamps are instantiated at RUNTIME, have the spawner call
//       TrackLightingController.Instance.RegisterLamp(lightComponent) right after each
//       Instantiate() - see RegisterLamp() below. This works regardless of spawn order
//       because Apply() is re-run any time a new lamp registers after Start().
public class TrackLightingController : MonoBehaviour
{
    public static TrackLightingController Instance { get; private set; }

    [Header("Skyboxes - assign a suitable one per time of day for THIS track")]
    public Material daySkybox;
    public Material eveningSkybox;
    public Material nightSkybox;

    [Header("Sun / Directional Light")]
    public Light sun;
    public Color daySunColor = Color.white;
    public Color eveningSunColor = new Color(1f, 0.6f, 0.3f);
    public Color nightSunColor = new Color(0.15f, 0.15f, 0.3f);
    [Range(0f, 3f)] public float dayIntensity = 1.2f;
    [Range(0f, 3f)] public float eveningIntensity = 0.5f;
    [Range(0f, 3f)] public float nightIntensity = 0.05f;

    [Header("Optional: rotate sun lower toward the horizon for evening/night")]
    public Vector3 dayRotationEuler = new Vector3(50f, -30f, 0f);
    public Vector3 eveningRotationEuler = new Vector3(10f, -30f, 0f);
    public Vector3 nightRotationEuler = new Vector3(-20f, -30f, 0f);

    [Header("Street Lamps - assign the PARENT object that holds all lamp lights (spline-spawned)")]
    [Tooltip("Drag the parent/holder GameObject here - every Light found in its children (at any depth) is auto-collected on Awake. No manual per-lamp dragging needed.")]
    public Transform streetLampsParent;
    private readonly List<Light> streetLamps = new List<Light>();

    [Header("Stud Lights - assign the PARENT object that holds all stud lights (building/track floodlights etc.)")]
    [Tooltip("Drag the parent/holder GameObject here - every Light found in its children (at any depth) is auto-collected on Awake.")]
    public Transform studLightsParent;
    private readonly List<Light> studLights = new List<Light>();

    // FIX (the grey-screen-at-night bug): RenderSettings.ambientIntensity, which the old
    // script relied on, is ONLY used when RenderSettings.ambientMode == AmbientMode.Skybox.
    // If your scene's Lighting > Environment > Source is set to Flat or Gradient (or if
    // DynamicGI.UpdateEnvironment() hasn't finished re-baking from the new skybox yet),
    // ambientIntensity does nothing and Unity keeps using whatever ambientLight color was
    // already sitting there - which showed up as a flat grey wash over the whole scene at
    // night, since there was no strong directional light left to compete with it.
    //
    // Fix: force AmbientMode.Flat and set RenderSettings.ambientLight directly to an actual
    // per-time-of-day color. This does not depend on skybox content, ambient mode, or async
    // GI baking timing - it's the same one call every time and always shows the right color.
    [Header("Ambient Light Color (replaces old ambientIntensity approach)")]
    [Tooltip("Flat ambient color used during the day. Keep this a light, neutral color.")]
    public Color dayAmbientColor = new Color(0.5f, 0.5f, 0.5f);
    [Tooltip("Flat ambient color used during evening. A dim warm tone works well.")]
    public Color eveningAmbientColor = new Color(0.25f, 0.18f, 0.18f);
    [Tooltip("Flat ambient color used at night. Keep this DARK (near-black/navy), not mid-grey, or you'll get the flat grey look again.")]
    public Color nightAmbientColor = new Color(0.03f, 0.03f, 0.07f);

    private TimeOfDay _currentTime;

    private void Awake()
    {
        Instance = this;
        CollectLights();
    }

    // Auto-collects every Light under streetLampsParent / studLightsParent, including
    // lights on spline-spawned children created before this script's Awake runs
    // (e.g. an edit-time spline tool that already baked the lamps into the scene).
    // If your spline spawner instantiates lamps at RUNTIME *after* Awake, just re-call
    // this (CollectLights()) once spawning finishes, or keep using RegisterLamp() below
    // for lamps that appear even later.
    private void CollectLights()
    {
        if (streetLampsParent == null)
            Debug.LogWarning("TrackLightingController: streetLampsParent is not assigned in the Inspector - street lamps can never be found or switched on.");
        if (studLightsParent == null)
            Debug.LogWarning("TrackLightingController: studLightsParent is not assigned in the Inspector - stud lights can never be found or switched on.");

        streetLamps.Clear();
        if (streetLampsParent != null)
            streetLamps.AddRange(streetLampsParent.GetComponentsInChildren<Light>(true));

        studLights.Clear();
        if (studLightsParent != null)
            studLights.AddRange(studLightsParent.GetComponentsInChildren<Light>(true));

        // DEBUG: tells you exactly what this script found each pass - check the Console.
        // 0/0 with both parents assigned means the lamp prefabs don't actually have a Light
        // component under them (or they're not children of the assigned parent yet).
        Debug.Log($"TrackLightingController: collected {streetLamps.Count} street lamp Light(s), {studLights.Count} stud Light(s).");
    }

    private void Start()
    {
        // FIX (lamps/studs not switching on at Evening/Night): CollectLights() ran in Awake(),
        // but if lamps are placed via a Spline Instantiate component (Unity Splines package),
        // that spawner may not have created its lamp GameObjects yet by the time Awake() ran -
        // Unity doesn't guarantee spawner-vs-this-script execution order. That left streetLamps/
        // studLights empty, so Apply() had nothing to actually turn on, no matter the time of day.
        // Fix: re-scan and re-apply a few times over the next second, so however long the spline
        // spawner takes to finish, this script eventually finds the real lamps and switches them.
        StartCoroutine(CollectAndApplyRepeatedly());
    }

    private IEnumerator CollectAndApplyRepeatedly()
    {
        Debug.Log($"TrackLightingController: applying time of day '{RaceSettings.SelectedTimeOfDay}'.");

        // Immediate pass - covers lamps that already existed at Awake (edit-time placement).
        CollectLights();
        Apply(RaceSettings.SelectedTimeOfDay);

        // A few delayed passes - covers lamps spawned at runtime shortly after this script starts.
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(0.2f);
            CollectLights();
            Apply(RaceSettings.SelectedTimeOfDay);
        }
    }

    // Only needed if your spline spawner creates lamps at RUNTIME *after* this script's
    // Awake/CollectLights already ran (so GetComponentsInChildren missed them because they
    // didn't exist yet). Call right after each lamp Instantiate():
    //   TrackLightingController.Instance?.RegisterLamp(spawnedLamp.GetComponentInChildren<Light>());
    // If lamps already exist in the scene under streetLampsParent before Play (edit-time
    // spline placement), you don't need to call this at all - CollectLights() finds them.
    public void RegisterLamp(Light lamp)
    {
        if (lamp == null || streetLamps.Contains(lamp)) return;
        streetLamps.Add(lamp);
        lamp.enabled = (_currentTime == TimeOfDay.Evening || _currentTime == TimeOfDay.Night);
    }

    // Same idea, for stud lights spawned at runtime after Awake.
    public void RegisterStudLight(Light light)
    {
        if (light == null || studLights.Contains(light)) return;
        studLights.Add(light);
        light.enabled = (_currentTime == TimeOfDay.Evening || _currentTime == TimeOfDay.Night);
    }

    public void Apply(TimeOfDay time)
    {
        _currentTime = time;

        // --- Skybox ---
        Material targetSkybox = time switch
        {
            TimeOfDay.Day => daySkybox,
            TimeOfDay.Evening => eveningSkybox,
            TimeOfDay.Night => nightSkybox,
            _ => daySkybox
        };
        if (targetSkybox != null)
        {
            RenderSettings.skybox = targetSkybox;
        }

        // --- Sun ---
        if (sun != null)
        {
            sun.color = time switch
            {
                TimeOfDay.Day => daySunColor,
                TimeOfDay.Evening => eveningSunColor,
                TimeOfDay.Night => nightSunColor,
                _ => daySunColor
            };

            sun.intensity = time switch
            {
                TimeOfDay.Day => dayIntensity,
                TimeOfDay.Evening => eveningIntensity,
                TimeOfDay.Night => nightIntensity,
                _ => dayIntensity
            };

            Vector3 rot = time switch
            {
                TimeOfDay.Day => dayRotationEuler,
                TimeOfDay.Evening => eveningRotationEuler,
                TimeOfDay.Night => nightRotationEuler,
                _ => dayRotationEuler
            };
            sun.transform.rotation = Quaternion.Euler(rot);
        }

        // --- Ambient light ---
        // FIX: force Flat ambient mode and set the color directly. This is what actually
        // fixes the grey-at-night bug - it no longer matters what ambient mode the scene
        // was authored with, and there's no dependency on GI bake timing after a skybox swap.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = time switch
        {
            TimeOfDay.Day => dayAmbientColor,
            TimeOfDay.Evening => eveningAmbientColor,
            TimeOfDay.Night => nightAmbientColor,
            _ => dayAmbientColor
        };

        // Still refresh environment reflections/GI to match the new skybox + ambient color
        // (safe to call regardless of ambient mode - only updates lighting data, doesn't
        // re-introduce the mode dependency the old ambientIntensity call had).
        DynamicGI.UpdateEnvironment();

        // --- Street lamps: on for Evening + Night, off for Day ---
        bool lampsOn = time == TimeOfDay.Evening || time == TimeOfDay.Night;
        foreach (var lamp in streetLamps)
        {
            if (lamp != null) lamp.enabled = lampsOn;
        }

        // --- Stud lights: same rule (on for Evening + Night, off for Day).
        // If you want these ONLY on for Evening (off again at Night), change the line
        // below to: bool studsOn = time == TimeOfDay.Evening;
        bool studsOn = time == TimeOfDay.Evening || time == TimeOfDay.Night;
        foreach (var stud in studLights)
        {
            if (stud != null) stud.enabled = studsOn;
        }

        // DEBUG: confirms this actually ran and what it set - check the Console. If the counts
        // here are 0, CollectLights() never found any real Light components (see its own log).
        // If the counts are > 0 and lampsOn/studsOn are true but lamps still look dark in the
        // Game view, the Light components ARE being enabled - the darkness is then coming from
        // something else (range too short, intensity too low, or an emissive material on the
        // lamp mesh that was never turned on - that's not a Light component this script can touch).
        Debug.Log($"TrackLightingController: Apply({time}) -> ambientColor={RenderSettings.ambientLight}, lampsOn={lampsOn} ({streetLamps.Count} lamps), studsOn={studsOn} ({studLights.Count} studs).");
    }
}