using UnityEngine;
using UnityEngine.SceneManagement;

// Attach this to the same GameObject as your `controller` script.
// It reads live values (engineRPM, KPH, IsBoosting, slip[], grounded, gearNum)
// and drives audio sources that get created automatically at runtime.
//
// You only need 4 AudioClips to start:
//   1. Engine Low  (idle / low RPM loop)
//   2. Engine High (redline / high RPM loop)
//   3. Boost       (looping whoosh/turbo sound)
//   4. Drift       (looping skid sound, only plays while actually drifting)
// Optional 5th clip: Gear Shift (one-shot clunk), since we already track gear changes.

[RequireComponent(typeof(AudioSource))]
public class VehicleAudio : MonoBehaviour
{
    [Header("References")]
    public controller car;       // your car controller script
    public InputManager IM;      // used for handbrake check

    [Header("Scene Restriction")]
    [Tooltip("Vehicle audio only plays while the active scene matches the track the player actually selected (RaceSettings.SelectedTrackScene) - so it stays silent in the garage/menu and the Score scene, and works automatically across Track1/Track2/Track3 without needing a fixed scene name here.")]
    public bool restrictToSelectedTrack = true;

    [Header("Engine Clips")]
    public AudioClip engineLowClip;
    public AudioClip engineHighClip;
    [Range(0.1f, 2f)]  public float minPitch = 0.7f;
    [Range(0.5f, 3f)]  public float maxPitch = 1.8f;
    public float engineCrossfadeSpeed = 5f;
    [Tooltip("How smoothly pitch follows RPM changes. Lower = more lag (heavy/natural feel). Higher = snappier. Recommended: 4-6.")]
    [Range(1f, 20f)] public float pitchSmoothing = 5f;  

    [Header("Boost Clip")]
    public AudioClip boostClip;
    public float boostFadeSpeed = 4f;
    [Range(0f, 1f)] public float boostMaxVolume = 0.8f;

    [Header("Drift Clip")]
    public AudioClip driftClip;
    [Tooltip("How much sideways slip counts as an actual drift before the sound kicks in. Lower = triggers on smaller/lighter drifts.")]
    public float driftSlipThreshold = 0.05f;
    [Tooltip("Slip value at which the drift sound reaches full volume. Keep this close to driftSlipThreshold so even light drifting sounds strong.")]
    public float driftFullVolumeSlip = 0.25f;
    public float driftFadeSpeed = 8f;
    [Range(0f, 1f)] public float driftMaxVolume = 1f;

    [Header("Optional: Gear Shift Clip")]
    public AudioClip gearShiftClip;
    [Range(0f, 1f)] public float gearShiftVolume = 0.6f;

    // Internal audio sources — created automatically at runtime
    private AudioSource engineLowSource;
    private AudioSource engineHighSource;
    private AudioSource boostSource;
    private AudioSource driftSource;
    private AudioSource gearShiftSource;

    [Header("Drift Fade")]
    public float driftFadeInSpeed  = 12f;   // how fast the sound ramps up when drift starts
    public float driftFadeOutSpeed = 4f;    // how fast it tails off when drift ends

    // FIX: track current smoothed pitch separately so we can lerp toward the target
    // instead of snapping the pitch directly to the raw RPM value every frame.
    private float currentPitch;

    private int lastGearNum;

    [Header("3D Sound (distance falloff)")]
    [Tooltip("How close (meters) you need to be to hear this car's sounds at full volume.")]
    public float minAudibleDistance = 5f;
    [Tooltip("Distance (meters) beyond which this car's sounds are fully inaudible.")]
    public float maxAudibleDistance = 40f;

    private void Awake()
    {
        if (car == null) car = GetComponent<controller>();
        if (IM  == null) IM  = GetComponent<InputManager>();

        engineLowSource  = CreateLoopingSource(engineLowClip);
        engineHighSource = CreateLoopingSource(engineHighClip);
        boostSource      = CreateLoopingSource(boostClip);
        driftSource      = CreateLoopingSource(driftClip);

        gearShiftSource             = gameObject.AddComponent<AudioSource>();
        gearShiftSource.clip        = gearShiftClip;
        gearShiftSource.loop        = false;
        gearShiftSource.playOnAwake = false;
        Setup3D(gearShiftSource);

        // Start with everything silent; engine loops are playing but at volume 0
        engineLowSource.volume  = 0f;
        engineHighSource.volume = 0f;
        boostSource.volume      = 0f;
        driftSource.volume      = 0f;

        engineLowSource.Play();
        engineHighSource.Play();

        // Initialise smoothed pitch to minPitch so there's no jump on first frame
        currentPitch = minPitch;

        lastGearNum = car != null ? car.gearNum : 0;
    }

    // Makes every AudioSource properly 3D/positional so only nearby cars are audible.
    // Without this, all AI car sounds play at full volume everywhere (the old bug).
    private void Setup3D(AudioSource src)
    {
        src.spatialBlend = 1f;                        // 0 = 2D, 1 = fully 3D/positional
        src.rolloffMode  = AudioRolloffMode.Linear;
        src.minDistance  = minAudibleDistance;
        src.maxDistance  = maxAudibleDistance;
    }

    private AudioSource CreateLoopingSource(AudioClip clip)
    {
        AudioSource src     = gameObject.AddComponent<AudioSource>();
        src.clip            = clip;
        src.loop            = true;
        src.playOnAwake     = false;
        Setup3D(src);
        return src;
    }

    private void Update()
    {
        if (car == null) return;

        // Only produce audio while in the track scene the player actually selected.
        // Using RaceSettings.SelectedTrackScene (instead of a fixed scene name) means this
        // works automatically for any track (Track1/Track2/Track3...) and stays silent in
        // every non-race scene (garage/menu, Score, etc.) without extra setup per scene.
        if (restrictToSelectedTrack && SceneManager.GetActiveScene().name != RaceSettings.SelectedTrackScene)
        {
            MuteAll();
            return;
        }

        // Engine loops get stopped by MuteAll() when we leave the scene,
        // so restart them once we're back in it.
        if (!engineLowSource.isPlaying)  engineLowSource.Play();
        if (!engineHighSource.isPlaying) engineHighSource.Play();

        HandleEngineSound();
        HandleBoostSound();
        HandleDriftSound();
        HandleGearShift();
    }

    private void MuteAll()
    {
        // Instantly silences and stops every source when outside the target scene
        // (e.g. menu, garage, loading screen) so nothing keeps looping in the background.
        engineLowSource.volume  = 0f;
        engineHighSource.volume = 0f;
        boostSource.volume      = 0f;
        driftSource.volume      = 0f;

        if (engineLowSource.isPlaying)  engineLowSource.Stop();
        if (engineHighSource.isPlaying) engineHighSource.Stop();
        if (boostSource.isPlaying)      boostSource.Stop();
        if (driftSource.isPlaying)      driftSource.Stop();
    }

    private void HandleEngineSound()
    {
        // Normalize current RPM: 0 (idle) → 1 (redline)
        float rpmNormalized = 0f;
        if (car.maxRPM > car.minRPM)
            rpmNormalized = Mathf.InverseLerp(car.minRPM, car.maxRPM, car.engineRPM);

        // Crossfade volumes between low and high clips
        float targetLowVolume  = 1f - rpmNormalized;
        float targetHighVolume = rpmNormalized;

        engineLowSource.volume  = Mathf.Lerp(engineLowSource.volume,  targetLowVolume,  Time.deltaTime * engineCrossfadeSpeed);
        engineHighSource.volume = Mathf.Lerp(engineHighSource.volume, targetHighVolume, Time.deltaTime * engineCrossfadeSpeed);

        // FIX: instead of snapping pitch directly to rpmNormalized every frame (which caused
        // the millisecond jumping), we lerp a persistent currentPitch toward the target.
        // pitchSmoothing controls how fast it chases: low values = smooth/laggy,
        // high values = snappy. Real engines can't change pitch instantaneously, so a
        // slight lag here actually makes it sound more convincing.
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, rpmNormalized);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * pitchSmoothing);

        engineLowSource.pitch  = currentPitch;
        engineHighSource.pitch = currentPitch;
    }

    private void HandleBoostSound()
    {
        bool  boosting     = car.IsBoosting;
        float targetVolume = boosting ? boostMaxVolume : 0f;

        boostSource.volume = Mathf.MoveTowards(boostSource.volume, targetVolume, boostFadeSpeed * Time.deltaTime);

        if      (boostSource.volume > 0.01f && !boostSource.isPlaying) boostSource.Play();
        else if (boostSource.volume <= 0.01f &&  boostSource.isPlaying) boostSource.Stop();
    }

    private void HandleDriftSound()
    {
        // Instant signal from the controller's own drift math (steering + speed) —
        // doesn't wait for physical tire slip to build up.
        float assistDrift = car.currentDriftAmount;

        // Real measured slip, used as a secondary/backup signal
        float maxSlip = 0f;
        if (car.slip != null)
        {
            for (int i = 0; i < car.slip.Length; i++)
            {
                float abs = Mathf.Abs(car.slip[i]);
                if (abs > maxSlip) maxSlip = abs;
            }
        }
        float slipVolume = Mathf.InverseLerp(driftSlipThreshold, driftFullVolumeSlip, maxSlip);

        bool handbraking = IM != null && IM.handbrake;

        // Take whichever signal is strongest: steering-based drift, actual slip, or handbrake
        float driftStrength = Mathf.Max(assistDrift, slipVolume);
        if (handbraking) driftStrength = 1f;

        bool  isDrifting   = car.grounded && driftStrength > 0.02f;
        float targetVolume = isDrifting ? driftStrength * driftMaxVolume : 0f;

        // Separate fade-in/out speeds: snappy fade-in for immediate feedback,
        // slower fade-out for a natural tail with no cutoff click.
        float speed = targetVolume > driftSource.volume ? driftFadeInSpeed : driftFadeOutSpeed;
        driftSource.volume = Mathf.MoveTowards(driftSource.volume, targetVolume, Time.deltaTime * speed);

        if      (driftSource.volume > 0.02f && !driftSource.isPlaying) driftSource.Play();
        else if (driftSource.volume <= 0.02f &&  driftSource.isPlaying) driftSource.Stop();
    }

    private void HandleGearShift()
    {
        if (gearShiftClip == null) return;

        if (car.gearNum != lastGearNum)
        {
            gearShiftSource.PlayOneShot(gearShiftClip, gearShiftVolume);
            lastGearNum = car.gearNum;
        }
    }
}