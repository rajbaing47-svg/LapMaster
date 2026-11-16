using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class camerController : MonoBehaviour
{
    public GameObject Player;
    public GameObject child;
    private controller RR;
    private Camera cam; // FIX: cached once instead of calling Camera.main every FixedUpdate (Camera.main does a hidden FindGameObjectWithTag internally)
    public float speed;
    public float defaultFOV = 0, desiredFOV = 0;
    [Range(0, 2)] public float smoothTime = 0;

    [Header("Motion Blur (scales with speed)")]
    [Tooltip("Drag your Post-process Volume (the one with the Motion Blur override on its profile) here.")]
    public PostProcessVolume postProcessVolume;
    [Tooltip("KPH at which motion blur reaches full strength - tune to your car's top speed.")]
    public float speedForMaxBlur = 150f;
    [Tooltip("Shutter angle at full speed - higher = more blur. 0 = off.")]
    [Range(0f, 360f)] public float maxShutterAngle = 270f;
    private MotionBlur motionBlur;

    [Header("Mouse Orbit (hold left click)")]
    [Tooltip("Degrees per pixel of mouse movement, horizontal.")]
    public float orbitYawSpeed = 200f;
    [Tooltip("Degrees per pixel of mouse movement, vertical.")]
    public float orbitPitchSpeed = 150f;
    [Tooltip("Clamp how far up/down you can orbit, in degrees.")]
    public float minPitch = -20f;
    public float maxPitch = 70f;

    private float orbitDistance;   // current camera-to-car distance, kept in sync by follow()
    private float yaw;             // current orbit yaw, in degrees
    private float pitch;           // current orbit pitch, in degrees
    private bool isOrbiting;       // true while LMB is held

    public void Start()
    {
        Player = GameObject.FindGameObjectWithTag("Player");

        // FIX: guard instead of crashing outright if Player isn't found yet (e.g. scene/load order changes later)
        if (Player == null)
        {
            Debug.LogError("camerController: no GameObject tagged 'Player' found in scene.");
            return;
        }

        // FIX: the code was searching for "sCameraConstraint" but the error message below said
        // "CameraConstraint" - if your child object is actually named "CameraConstraint" (no leading
        // 's'), the Find() above always failed silently and this whole script quietly did nothing.
        // Now it tries both names so it works either way - but go check your Hierarchy and make sure
        // the child under your Player car is named exactly one of these two.
        Transform constraint = Player.transform.Find("CameraConstraint");
        if (constraint == null) constraint = Player.transform.Find("sCameraConstraint");
        if (constraint == null)
        {
            Debug.LogError("camerController: no 'CameraConstraint' (or 'sCameraConstraint') child found on Player. " +
                            "Add an empty child GameObject under your car with one of these exact names, " +
                            "positioned where you want the default camera anchor to be.");
            return;
        }
        child = constraint.gameObject;

        RR = Player.GetComponent<controller>();
        cam = Camera.main;

        // FIX: pull the Motion Blur settings out of the assigned volume's profile once, so
        // FixedUpdate can just tweak its shutterAngle value every frame without re-fetching it.
        if (postProcessVolume != null && postProcessVolume.profile != null)
        {
            if (!postProcessVolume.profile.TryGetSettings(out motionBlur))
            {
                Debug.LogWarning("camerController: assigned Post-process Volume has no Motion Blur override on its profile. Add one via Add Override > Unity > Motion Blur.");
            }
        }
        else
        {
            Debug.LogWarning("camerController: Post Process Volume field is empty - drag your Post-process Volume GameObject into it in the Inspector, or motion blur will never apply.");
        }
    }

    private void Update()
    {
        // Mouse input is read in Update (not FixedUpdate) so we don't miss/duplicate deltas
        // depending on the physics timestep - we just accumulate yaw/pitch here and apply
        // the actual camera transform in FixedUpdate below.
        isOrbiting = Input.GetMouseButton(0); // left click, GTA5-style free look

        if (isOrbiting)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            yaw += mouseX * orbitYawSpeed * Time.deltaTime;
            pitch -= mouseY * orbitPitchSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void FixedUpdate()
    {
        if (Player == null || child == null || RR == null) return; // FIX: don't run follow/FOV logic if setup failed

        if (isOrbiting)
            orbit();
        else
            follow();

        boostFOV();
        updateMotionBlur();
        //speed = (RR.KPH >= 50)? 20 : RR.KPH/4;
    }

    public void follow()
    {
        if (speed <= 23)
            speed = Mathf.Lerp(speed, RR.KPH / 2, Time.fixedDeltaTime); // FIX: fixedDeltaTime, since this runs in FixedUpdate
        else
            speed = 23;

        // FIX: exponential smoothing instead of a raw Lerp(dt*speed) - tracks the target tightly
        // without the mushy "always half a step behind" feel raw delta-time lerping has.
        float t = 1f - Mathf.Exp(-speed * Time.fixedDeltaTime);
        transform.position = Vector3.Lerp(transform.position, child.transform.position, t);
        transform.LookAt(Player.transform.position);

        // Keep yaw/pitch and the remembered distance in sync with wherever "follow" naturally
        // puts the camera, so when you hold left click, orbit starts from exactly where the
        // camera already is - same distance, same angle, no jump.
        Vector3 offset = transform.position - Player.transform.position;
        if (offset.sqrMagnitude > 0.0001f)
        {
            orbitDistance = offset.magnitude;
            Quaternion lookRot = Quaternion.LookRotation(offset.normalized);
            yaw = lookRot.eulerAngles.y;
            pitch = lookRot.eulerAngles.x;
        }
    }

    private void orbit()
    {
        // GTA5-style free look: spins around the car based on mouse movement, always at
        // orbitDistance away (whatever the camera's normal follow distance already was -
        // captured the moment you started holding left click, not some separate/added value).
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = Player.transform.position + rot * (Vector3.back * orbitDistance);
        transform.LookAt(Player.transform.position);
    }

    private void updateMotionBlur()
    {
        if (motionBlur == null) return;

        // Force these on from script - so even if the per-parameter override checkbox in the
        // Inspector wasn't ticked, the effect still actually applies at runtime.
        motionBlur.active = true;
        motionBlur.enabled.overrideState = true;
        motionBlur.enabled.value = true;
        motionBlur.shutterAngle.overrideState = true;

        // 0 at a standstill, ramps up to maxShutterAngle by speedForMaxBlur - so parking/slow
        // corners stay crisp and only real speed feels blurred.
        float t = Mathf.Clamp01(RR.KPH / speedForMaxBlur);
        motionBlur.shutterAngle.value = Mathf.Lerp(0f, maxShutterAngle, t);
    }

    private void boostFOV()
    {
        if (cam == null) return;

        if (Input.GetKey(KeyCode.LeftShift))
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, desiredFOV, Time.fixedDeltaTime * smoothTime);
        else
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, defaultFOV, Time.fixedDeltaTime * smoothTime);
    }
}