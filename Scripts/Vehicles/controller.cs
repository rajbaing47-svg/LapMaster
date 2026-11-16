using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


public class controller : MonoBehaviour
{
    internal enum driveType
    {
        FrontWheelDrive,
        RearWheelDrive,
        AllWheelDrive
    }
   [SerializeField] private driveType DriveType;

    [Header("Scene Restriction")]
    [Tooltip("The car will only accept input / move while the active scene matches the currently selected track (RaceSettings.SelectedTrackScene). In any other scene (e.g. Showroom/garage or the Score scene) it stays frozen in place. Untick to disable the check entirely.")]
    public bool restrictToSelectedTrack = true;

    [Header("Variables")]
    public float handBrakeFriction = 0f;

    public float handBrakeFrictionMultiplier = 2f;
    public GameManager manager;
    public float MotorTorque = 500f; // NOTE: currently unused by the physics calc - see enginePowerMultiplier below for the field that actually affects power.

    [Tooltip("NEW: multiplies the car's actual power output (EnginePower curve x gear ratio) every frame. This is the field that genuinely affects pickup/acceleration - MotorTorque above does NOT, it's not wired into the physics calculation. 1 = stock power, 1.5-2 = noticeably stronger pickup.")]
    public float enginePowerMultiplier = 1.5f;
    public GameObject wheelMeshes, wheelColliders;
    public float steeringMax = 30;
    public float radius = 6;
    public float DownForceValue = 50;
    public float KPH;
    public float brakePower;    

    [Tooltip("NEW: fraction of full brake force sent to the front wheels while braking (rear always gets full force). Lower = more grip/steering retained through the front wheels while braking, at the cost of slightly longer stopping distance. 0.5-0.7 is a good range - keeps the car turnable while slowing down instead of understeering straight on.")]
    [Range(0f, 1f)]
    public float frontBrakeBias = 0.6f;
    public GameObject CenterOfMass;
    public bool reverse = false;
    public float thrust = 1000f;
    public float smoothTime =  0.01f;
    public float totalPower;
    public float wheelsRPM;
    public float engineRPM;
    public float maxRPM ,minRPM;

    [Header("Top Speed Limit")]
    [Tooltip("Hard cap on vehicle speed in KPH. Car will never exceed this, even with boost, drift-assist force, or handbrake push force. Keep this in sync with GameManager's maxSpeed so the speedometer dial maxes out exactly when the car actually hits its cap.")]
    public float maxSpeedKph = 280f; // UPDATED: raised from 230 for a higher top speed - remember to keep GameManager's speedometer max in sync, per the tooltip above.

    // Live cap after ApplyShowroomStats — GameManager reads this at race start so the speedo dial matches this car.
    public float CurrentMaxSpeedKph => maxSpeedKph;

    [Header("Boost")]
    [Tooltip("NEW: multiplies the car's normal engine power while boosting - e.g. 2 = boost roughly DOUBLES actual power output, on top of the flat boostPower add below. This is what makes boost feel like a real power increase instead of just a small flat push.")]
    public float boostPowerMultiplier = 2f;
    public float boostPower = 2000f;      // max extra power boost can add
    public float boostBuildRate = 4000f;  // power gained per second while boosting
    public float boostDecayRate = 6000f;  // power lost per second when not boosting
    private float currentBoost = 0f;
    public bool IsBoosting => currentBoost > 0f; // exposed read-only so other scripts (e.g. audio) can react to boost state

    // Exposed for other scripts (e.g. DriftSmokeEffect.cs) - IM itself is private, so this is
    // the safe read-only way for external scripts to know if the handbrake is currently held.
    public bool IsHandbraking => IM != null && IM.handbrake;

    // Exposed for other scripts (e.g. CarLightsController.cs) - isBrakingNow itself is local
    // to moveVehicle(), this is the safe read-only way for external scripts to know if the
    // car is currently under real braking (not just engine braking or handbrake).
    public bool IsBraking { get; private set; }

    // Exposed for other scripts (e.g. TireSkidMarks.cs) - wheels[] itself is private,
    // this is the safe read-only way for external scripts to reach each WheelCollider.
    // Index layout: 0 = front-left, 1 = front-right, 2 = rear-left, 3 = rear-right
    // (matches the existing handbrake/steer code elsewhere in this file).
    public WheelCollider GetWheel(int i) => wheels[i];

    // Exposed alongside GetWheel - the mesh-based skid system needs each wheel's current
    // world position (the visual mesh transform, already updated every frame in
    // animateWheels()), not just the WheelCollider itself.
    public Vector3 GetWheelWorldPosition(int i) => wheelMesh[i].transform.position;

    [Header("Boost Cooldown")]
    [Tooltip("How long (seconds) the player must wait after pressing boost before they can use it again.")]
    public float boostCooldownDuration = 5f;
    private bool boostOnCooldown = false;
    private float boostCooldownTimer = 0f;
    private bool wasBoosting = false;

    // FIX (boost exploit): previously the "ramp currentBoost up" check only looked at
    // (IM.boosting && boostOnCooldown). boostOnCooldown stays true for the WHOLE cooldown
    // window, not just the original press - so releasing the boost key early and pressing it
    // again before the cooldown finished let the player re-ramp currentBoost back up to full
    // and get a second free burst of thrust, completely bypassing the cooldown's purpose.
    // This flag tracks the single continuous hold that actually triggered the cooldown - once
    // that hold is released, no further press can produce thrust again until boostOnCooldown
    // clears naturally.
    private bool boostCurrentlyActive = false;

    // Exposed for UI (e.g. BoostUI.cs)
    public bool IsBoostOnCooldown => boostOnCooldown;
    public float BoostCooldownRemaining => boostCooldownTimer;
    // 0 = just used (empty), 1 = fully recharged/ready
    public float BoostCooldownProgress01 => boostOnCooldown ? 1f - (boostCooldownTimer / boostCooldownDuration) : 1f;

    // Maps Showroom 0-10 bars onto the four fields that actually change how the car drives.
    // 8 on each bar matches this prefab's stock values (280 kph / 1.5 power / 35 steer / 2500 brake).
    public void ApplyShowroomStats(float topSpeed, float acceleration, float handling, float braking)
    {
        maxSpeedKph = MapShowroomStat(topSpeed, 200f, 280f);
        enginePowerMultiplier = MapShowroomStat(acceleration, 0.85f, 1.5f);
        steeringMax = MapShowroomStat(handling, 22f, 35f);
        brakePower = MapShowroomStat(braking, 1200f, 2500f);
    }

    private static float MapShowroomStat(float stat0to10, float valueAt0, float valueAt8)
    {
        float t = Mathf.Clamp(stat0to10, 0f, 10f) / 8f;
        return valueAt0 + (valueAt8 - valueAt0) * t;
    }


    // FIX: reverse-detection deadzone so we don't flicker gears near 0 speed
    public float reverseDeadzone = 1f;

    internal enum gearBox
    {
        Automatic,
        manual
    }
    [SerializeField]private gearBox gearChange;
    public float[] gears;
    public int gearNum = 0;

    [Header("Gear Shift Cooldown")]
    [Tooltip("Minimum seconds between gear shifts. Without this, engineRPM hovering right at maxRPM/minRPM makes the Automatic gearbox flip gearNum up and down every frame ('gear hunting') - each flip fires the gear-shift sound even though it's not a real shift.")]
    public float gearShiftCooldown = 0.4f;
    private float gearShiftTimer = 0f;

    public AnimationCurve EnginePower;

    [Header("DEBUG")]
    public float[] slip =  new float[4];
    public bool grounded; // FIX: now actually stores isGrounded() result
    private InputManager IM;
    private WheelCollider[] wheels = new WheelCollider[4];
    private GameObject[] wheelMesh = new GameObject[4]; 
    private Rigidbody rb;

    // FIX (wheel flips 180 in Play mode): some wheel meshes (typically the left-side ones)
    // have a baked-in local rotation offset - e.g. Rotation Y = 180 - used to visually mirror
    // the mesh without needing a negative scale (which is the correct way to do it, and avoids
    // inverted normals). The bug was that animateWheels() overwrote wheelMesh.transform.rotation
    // outright every frame with the WheelCollider's raw world rotation, completely discarding that
    // baked offset the instant Play mode started - which is exactly why it looked correct in the
    // Scene view / prefab (nothing had touched it yet) but flipped as soon as physics ran.
    // Fix: capture each wheel's rotation offset ONCE, relative to its WheelCollider, before
    // anything overwrites it - then re-apply that same offset on top of the live collider
    // rotation every frame instead of replacing it.
    private Quaternion[] wheelMeshRotationOffset = new Quaternion[4];

    public WheelFrictionCurve forwardFriction,sidewaysFriction;

    // FIX: SmoothDamp needs persistent velocity refs across frames, not locals
    private float engineRpmVelocity;
    private float frictionVelocity;
    
    //Car Shop Values 
    public string carName;
    public int carPrice ;

    [Header("Drift Feel")]
    [Tooltip("0 = no extra drift, 1 = very drifty. Keep this LOW (0.15–0.3) for a subtle slide.")]
    [Range(0f, 1f)]
    public float driftIntensity =0.12f;

    [Tooltip("KPH before drift-assist starts loosening the rear end.")]
    public float driftMinSpeed = 35f;

    [Tooltip("KPH range over which the effect ramps up to full strength.")]
    public float driftRampRange = 80f;
    public float driftForceAssist = 3000f;
    public float maxSpinRate = 2.5f;

    [HideInInspector] public float currentDriftAmount = 2f; // 0-1, how hard the drift-assist is currently engaged

    [Header("Handbrake Grip")]
    [Tooltip("FIX: replaces the old 'driftFactor' field, which was declared but never assigned anywhere - it always stayed at its default 0, which meant handBrakeFrictionMultiplier had ZERO effect while handbraking (0 * anything is still 0). This is the actual baseline grip fraction the rear tires smoothly damp toward while the handbrake is held, and it's now genuinely scaled by handBrakeFrictionMultiplier below, so that field finally does something during a handbrake slide.")]
    [Range(0f, 1f)]
    public float handbrakeGripFactor = 0.05f;

    // FIX: guards against NullReferenceExceptions in FixedUpdate if a required child object
    // (mass / WheelMeshes / WheelColliders, or any of the 4 individual wheel transforms) wasn't
    // found in getObjects(). Previously those cases only logged a Debug.LogError and then let
    // execution continue straight into wheelRPM()/animateWheels(), which would NRE the moment
    // FixedUpdate ran. Now the car freezes safely (same as the scene/race-not-started checks)
    // instead of throwing.
    private bool referencesValid = true;


    // Start is called before the first frame update
    private void Awake()
    {
        
        manager = FindObjectOfType<GameManager>();
        getObjects();
        StartCoroutine(timedLoop()); // FIX: start the coroutine once instead of calling it every FixedUpdate
    }

    
    // DEBUG: so freezing has a visible reason in the Console instead of failing silently
    private bool loggedSceneMismatch = false;
    private bool loggedRaceNotStarted = false;
    private bool loggedInvalidReferences = false;

    private void FixedUpdate()
    {
        // FIX: previously this only blocked "MenuScene" and ran (and accepted input) everywhere else,
        // including a podium/score scene where the car isn't supposed to move at all.
        // Now the car only drives while the active scene matches the currently selected track,
        // and gets frozen in place otherwise. Reads RaceSettings.SelectedTrackScene live each frame
        // instead of a fixed sceneName, so one shared car prefab drives correctly across
        // Track1/Track2/Track3 and stays frozen in Showroom/garage and Score scenes.
        if (restrictToSelectedTrack && SceneManager.GetActiveScene().name != RaceSettings.SelectedTrackScene)
        {
            // DEBUG: logs once per scene load instead of spamming every FixedUpdate
            if (!loggedSceneMismatch)
            {
                Debug.LogWarning($"{name}: frozen because active scene is '{SceneManager.GetActiveScene().name}' " +
                                  $"but the currently selected track is '{RaceSettings.SelectedTrackScene}'. " +
                                  $"They must match exactly (case-sensitive) for the car to move.");
                loggedSceneMismatch = true;
            }
            FreezeCar();
            return;
        }
        loggedSceneMismatch = false;

        if (RaceManager.Instance != null && !RaceManager.Instance.RaceStarted)
        {
            // DEBUG: logs once instead of spamming every FixedUpdate
            if (!loggedRaceNotStarted)
            {
                Debug.LogWarning($"{name}: frozen because RaceManager.Instance.RaceStarted is false. " +
                                  $"Check the Console for a 'RaceManager: ... not assigned' error - if any of " +
                                  $"playerPrefab / aiPrefab / playerStart / aiStart aren't set on the RaceManager " +
                                  $"in the Inspector, the countdown never runs and RaceStarted never becomes true.");
                loggedRaceNotStarted = true;
            }
            FreezeCar();
            return;
        }
        loggedRaceNotStarted = false;

        // FIX: bail out before touching any wheel/rigidbody data if required references are missing,
        // instead of NRE-ing partway through the physics step.
        if (!referencesValid)
        {
            if (!loggedInvalidReferences)
            {
                Debug.LogError($"{name}: frozen because one or more required child objects (mass / WheelMeshes / WheelColliders / wheel transforms) were missing. See the Debug.LogError messages from getObjects() above and fix the hierarchy.");
                loggedInvalidReferences = true;
            }
            FreezeCar();
            return;
        }

        calculateEnginePower();
        animateWheels();     
        moveVehicle();
        steerVehicle();
        addDownForce();
        getFriction();
        shifter();
        grounded = isGrounded(); 
        adjustTraction();
        ClampTopSpeed(); // FIX: hard speed cap - must run LAST, after every force this frame
                         // (boost, drift-assist, handbrake forward push) so nothing can sneak
                         // the car's velocity past maxSpeedKph before the frame ends.
    }

    // FIX (speed cap): caps the car's actual rigidbody velocity so KPH can never exceed
    // maxSpeedKph, no matter how much boost/drift/handbrake force was added this frame.
    // Only scales the magnitude, direction is untouched, so it doesn't fight steering.
    private void ClampTopSpeed()
    {
        float maxSpeedMs = maxSpeedKph / 3.6f;
        if (rb.velocity.magnitude > maxSpeedMs)
        {
            rb.velocity = rb.velocity.normalized * maxSpeedMs;
        }
    }

    // FIX: new - keeps the car completely still and unresponsive to input while outside the
    // selected track scene (e.g. sitting on a podium in a score/menu scene). Zeroes physics
    // velocity so it doesn't keep drifting from momentum it had when the previous scene unloaded,
    // and zeroes wheel torque/steer so nothing is silently accumulating for when the car re-enters
    // the race scene.
    private void FreezeCar()
    {
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (wheels != null)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                wheels[i].motorTorque = 0f;
                wheels[i].brakeTorque = brakePower; // hold it in place with the brakes on
                wheels[i].steerAngle = 0f;
            }
        }

        totalPower = 0f;
    }

    private void calculateEnginePower()
    {
        wheelRPM();

        totalPower = EnginePower.Evaluate(engineRPM) * (gears[gearNum]) * IM.vertical * enginePowerMultiplier;

        // FIX: reuse a persistent velocity field so SmoothDamp actually smooths over time
        engineRPM = Mathf.SmoothDamp(engineRPM, 500 + (Mathf.Abs(wheelsRPM) * 3.6f * (gears[gearNum])), ref engineRpmVelocity, smoothTime);
    }

    private void wheelRPM()
    {
        float sum = 0;

        for (int i = 0; i < wheels.Length; i++)
        {
            sum += wheels[i].rpm;
        }
        wheelsRPM = sum / wheels.Length; // FIX: removed pointless counter, always divides by 4

        // FIX: added a small deadzone so we don't flip forward/reverse from tiny fluctuations near 0 speed
        if (wheelsRPM < -reverseDeadzone && !reverse)
        {
            reverse = true;
            manager.changeGear();
        }
        else if (wheelsRPM > reverseDeadzone && reverse)
        {
            reverse = false;
            manager.changeGear();
        }
    }

    private void shifter()
    {
        // FIX (gear sound spam): tick the cooldown down every frame regardless of mode,
        // so manual shifts also respect it (stops you from spamming Q/E faster than the
        // sound/animation can keep up, same fix as the automatic hunting below).
        if (gearShiftTimer > 0f) gearShiftTimer -= Time.fixedDeltaTime;

        //Automatic
        if (gearChange == gearBox.Automatic)
        {
            if (gearShiftTimer > 0f) return; // still in cooldown from the last shift - skip this frame entirely

            if(engineRPM > maxRPM && gearNum < gears.Length - 1 && !reverse) 
            {
                gearNum++;
                gearShiftTimer = gearShiftCooldown;
                manager.changeGear();
            }
            else if(engineRPM < minRPM && gearNum > 0)
            {
                gearNum--;
                gearShiftTimer = gearShiftCooldown;
                manager.changeGear();
            }
        }
        //Manual
        // FIX: wrapped manual-only logic in braces - previously the auto-downshift check below
        // ran every frame regardless of mode because it wasn't actually part of the else branch
        else
        {
            if (gearShiftTimer > 0f) return;

            if (Input.GetKeyDown(KeyCode.E))
            {
                // FIX: clamp so gearNum can never exceed the array bounds (was throwing IndexOutOfRange)
                gearNum = Mathf.Clamp(gearNum + 1, 0, gears.Length - 1);
                gearShiftTimer = gearShiftCooldown;
                manager.changeGear();
            }
            else if (Input.GetKeyDown(KeyCode.Q))
            {
                gearNum = Mathf.Clamp(gearNum - 1, 0, gears.Length - 1);
                gearShiftTimer = gearShiftCooldown;
                manager.changeGear();
            }
        }
    }   

    private bool isGrounded()
    {
        return wheels[0].isGrounded && wheels[1].isGrounded && wheels[2].isGrounded && wheels[3].isGrounded;
    }
    private void addDownForce()
    {
        rb.AddForce(-transform.up *DownForceValue * rb.velocity.magnitude);
    }
    private void moveVehicle()
    {
        if(DriveType == driveType.AllWheelDrive)
        {
             for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].motorTorque = totalPower/4;
            }
        }else if (DriveType == driveType.RearWheelDrive)
        {
            for (int i = 2; i < wheels.Length; i++)
            {
                wheels[i].motorTorque = totalPower/2;
            }  
        }else
        {
              for (int i = 0; i < wheels.Length - 2; i++)
            {
                wheels[i].motorTorque = totalPower/2;
            }  
        }

        KPH = rb.velocity.magnitude*3.6f;

        // ---- BOOST + COOLDOWN ----
        // Rising-edge detect: the moment boost is first pressed (and we're not already on
        // cooldown), lock it in, start the cooldown timer, and mark this hold as the one
        // allowed to actually produce thrust this cycle.
        if (IM.boosting && !wasBoosting && !boostOnCooldown)
        {
            boostOnCooldown = true;
            boostCooldownTimer = boostCooldownDuration;
            boostCurrentlyActive = true;
        }
        wasBoosting = IM.boosting;

        // FIX (boost exploit): the moment the button is released, this hold is spent - any
        // further press before boostOnCooldown naturally clears produces no thrust. Without this,
        // release-then-repress during the same cooldown window let currentBoost ramp back up to
        // full again, giving effectively unlimited boost.
        if (!IM.boosting)
        {
            boostCurrentlyActive = false;
        }

        // Tick the cooldown down every physics step, regardless of input.
        if (boostOnCooldown)
        {
            boostCooldownTimer -= Time.fixedDeltaTime;
            if (boostCooldownTimer <= 0f)
            {
                boostOnCooldown = false;
                boostCooldownTimer = 0f;
            }
        }

        // Boost only ramps up during the single continuous hold that triggered the cooldown.
        if (boostCurrentlyActive)
        {
            currentBoost = Mathf.MoveTowards(currentBoost, boostPower, boostBuildRate * Time.fixedDeltaTime);
        }
        else
        {
            currentBoost = Mathf.MoveTowards(currentBoost, 0f, boostDecayRate * Time.fixedDeltaTime);
        }
        // FIX (boost feels weak): previously boost only ADDED a flat currentBoost amount on top
        // of totalPower, which barely moved the needle at higher speeds where totalPower is
        // already large. Now, while actively boosting, totalPower is also MULTIPLIED by
        // boostPowerMultiplier (default 2x = real power output roughly doubles), then the flat
        // currentBoost is still added on top for a strong low-speed kick too.
        if (currentBoost > 0f)
        {
            totalPower *= boostPowerMultiplier;
        }
        totalPower += currentBoost;
        // ---- END BOOST + COOLDOWN ----

        // FIX (real braking): previously "braking" only ever meant reversing motorTorque via
        // negative IM.vertical (engine braking) - actual wheels[i].brakeTorque was NEVER applied
        // for normal braking, only for the handbrake. That's fine at low speeds/power, but once
        // top speed and engine power were raised, engine braking alone couldn't decelerate fast
        // enough for tight corners - the AI's braking logic was working correctly, the car just
        // had no strong enough physical way to actually slow down, so it kept sliding into walls.
        //
        // Now: if vertical is negative AND the car is still actually moving forward (not already
        // near-stopped/reversing), that negative input is treated as REAL braking - apply genuine
        // brakeTorque (scaled by how hard vertical is asking to brake) to all four wheels, and
        // zero out motorTorque so the engine doesn't fight the brakes. Only once speed drops below
        // reverseDeadzone does a held-negative-vertical input actually engage reverse drive, same
        // as before.
        // FIX (cars missing waypoints after braking): applying full brakeTorque to all 4 wheels
        // with no anti-lock behavior locks the wheels completely under hard braking - a locked
        // (non-rotating) wheel loses most of its cornering/lateral grip, which is exactly why cars
        // started sliding straight through corners instead of turning at the waypoint the moment
        // they braked hard for it. Real cars have ABS for this same reason.
        //
        // Simplified ABS: front wheels (0,1) get less brake force than rear (frontBrakeBias), since
        // steering happens through the front wheels and they need to keep enough grip/rotation to
        // actually turn while slowing down. On top of that, any wheel whose rpm has dropped near
        // zero while the car is still moving at real speed (i.e. it's locked/skidding) has its
        // brake force cut in half for that frame, letting it spin up again and regain grip - a
        // crude but effective per-wheel ABS pulse.
        bool isBrakingNow = IM.vertical < -0.05f && !reverse && KPH > reverseDeadzone;
        IsBraking = isBrakingNow;

        if (isBrakingNow)
        {
            float brakeAmount = Mathf.Clamp01(-IM.vertical) * brakePower;
            for (int i = 0; i < wheels.Length; i++)
            {
                float wheelBrake = brakeAmount * (i < 2 ? frontBrakeBias : 1f);

                bool wheelLocked = Mathf.Abs(wheels[i].rpm) < 5f && KPH > 15f;
                if (wheelLocked) wheelBrake *= 0.5f;

                wheels[i].motorTorque = 0f;
                wheels[i].brakeTorque = wheelBrake;
            }
        }
        else if (IM.handbrake)
        {
            wheels[3].brakeTorque = wheels[2].brakeTorque = brakePower;
            wheels[1].brakeTorque = wheels[0].brakeTorque = 0f;
        }else
        {
            wheels[3].brakeTorque = wheels[2].brakeTorque = 0f;
            wheels[1].brakeTorque = wheels[0].brakeTorque = 0f;
        }

    }   

    private void steerVehicle()
    {

        if(IM.horizontal > 0)
        {
            wheels[0].steerAngle = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.55f/ (radius + (1.5f /2))) * IM.horizontal, -steeringMax, steeringMax);
            wheels[1].steerAngle = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.55f/ (radius - (1.5f /2))) * IM.horizontal, -steeringMax, steeringMax);
        }else if (IM.horizontal < 0)
        {
            wheels[0].steerAngle = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.55f/ (radius - (1.5f /2))) * IM.horizontal, -steeringMax, steeringMax);
            wheels[1].steerAngle = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.55f/ (radius + (1.5f /2))) * IM.horizontal, -steeringMax, steeringMax);
        }else
        {
            wheels[0].steerAngle = 0;
            wheels[1].steerAngle = 0;
        }
        // FIX: steeringMax was declared but never used - now both angles are clamped to it
    }

    private void animateWheels()
    {
        Vector3 wheelPosition = Vector3.zero;
        Quaternion wheelRotation = Quaternion.identity;

        for(int i =0; i < 4; i++)
        {
            wheels [i].GetWorldPose(out wheelPosition, out wheelRotation);
            wheelMesh [i].transform.position = wheelPosition;

            // FIX (wheel flip): re-apply the mesh's original baked mirror offset on top of the
            // collider's live rotation, instead of overwriting rotation outright. Previously this
            // line was just "wheelMesh[i].transform.rotation = wheelRotation;", which discarded
            // any baked offset (e.g. a left-side wheel modeled/rotated 180 on Y to mirror it)
            // the moment Play mode started - looked fine in the Scene view/prefab since nothing
            // had touched it yet, then flipped as soon as this method started running.
            wheelMesh [i].transform.rotation = wheelRotation * wheelMeshRotationOffset[i];
        }
    }

       private void getObjects()
    {
        IM = GetComponent<InputManager>();
        rb = GetComponent<Rigidbody>();

        referencesValid = true; // FIX: assume valid, then flag false on any missing piece below

        Transform massT = transform.Find("mass");
        if (massT == null)
        {
            Debug.LogError($"{name}: could not find child object 'mass'. Check it's a direct child of the car root.");
            referencesValid = false;
        }
        else
        {
            CenterOfMass = massT.gameObject;
            rb.centerOfMass = CenterOfMass.transform.localPosition;
        }

        Transform wheelMeshesT = transform.Find("Wheels/WheelMeshes");
        if (wheelMeshesT == null)
        {
            Debug.LogError($"{name}: could not find child object 'WheelMeshes'. Check it's a direct child of the car root.");
            referencesValid = false;
        }
        else
        {
            wheelMeshes = wheelMeshesT.gameObject;

            // FIX: each individual wheel-mesh child (0-3) is now checked instead of assumed to
            // exist - a missing one used to NRE the first time animateWheels() ran.
            for (int i = 0; i < 4; i++)
            {
                Transform meshChild = wheelMeshesT.Find(i.ToString());
                if (meshChild == null)
                {
                    Debug.LogError($"{name}: could not find child object '{i}' under WheelMeshes.");
                    referencesValid = false;
                }
                else
                {
                    wheelMesh[i] = meshChild.gameObject;
                }
            }
        }



        Transform wheelCollidersT = transform.Find("Wheels/WheelColliders");
        if (wheelCollidersT == null)
        {
            Debug.LogError($"{name}: could not find child object 'Wheels/WheelColliders'. Check it's a direct child of the car root.");
            referencesValid = false;
        }
        else
        {
            wheelColliders = wheelCollidersT.gameObject;

            // FIX: each individual wheel-collider child (0-3) is now checked instead of assumed
            // to exist - a missing one used to NRE the first time wheelRPM()/isGrounded() ran.
            for (int i = 0; i < 4; i++)
            {
                Transform colliderChild = wheelCollidersT.Find(i.ToString());
                if (colliderChild == null)
                {
                    Debug.LogError($"{name}: could not find child object '{i}' under WheelColliders.");
                    referencesValid = false;
                }
                else
                {
                    WheelCollider wc = colliderChild.GetComponent<WheelCollider>();
                    if (wc == null)
                    {
                        Debug.LogError($"{name}: child '{i}' under WheelColliders has no WheelCollider component.");
                        referencesValid = false;
                    }
                    wheels[i] = wc;
                }
            }
        }

        if (IM == null)
        {
            Debug.LogError($"{name}: no InputManager component found on this GameObject - required for driving input.");
            referencesValid = false;
        }
        if (rb == null)
        {
            Debug.LogError($"{name}: no Rigidbody component found on this GameObject - required for physics.");
            referencesValid = false;
        }

        // FIX (wheel flip): now that both wheels[] and wheelMesh[] are populated, capture each
        // mesh's current rotation relative to its own WheelCollider - this preserves whatever
        // baked mirror offset (e.g. Rotation Y = 180 on a left-side wheel) the artist set up,
        // so animateWheels() can re-apply it every frame instead of wiping it out.
        for (int i = 0; i < 4; i++)
        {
            if (wheels[i] == null || wheelMesh[i] == null) continue;
            wheelMeshRotationOffset[i] = Quaternion.Inverse(wheels[i].transform.rotation) * wheelMesh[i].transform.rotation;
        }
    }

    private void getFriction()
    {
        for (int i = 0; i <wheels.Length; i++)
        {
            WheelHit wheelHit;
            wheels[i].GetGroundHit(out wheelHit);
            slip[i] = wheelHit.sidewaysSlip;

        }
    }

  private void adjustTraction(){
    float driftSmoothFactor = 0.7f * Time.deltaTime;

    if(IM.handbrake){
        sidewaysFriction = wheels[0].sidewaysFriction;
        forwardFriction = wheels[0].forwardFriction;

        // FIX: this used to SmoothDamp toward "driftFactor * handBrakeFrictionMultiplier" where
        // driftFactor was a private field that was NEVER assigned anywhere in the class - it
        // silently sat at its default 0f forever, so the target was always 0 regardless of
        // handBrakeFrictionMultiplier's value. That made handBrakeFrictionMultiplier completely
        // inert during a handbrake slide even though it's a public, tunable, tooltip-documented
        // field used elsewhere in this same method. Now the target is a real tunable baseline
        // (handbrakeGripFactor) scaled by the multiplier, so the multiplier actually does something.
        sidewaysFriction.extremumValue = sidewaysFriction.asymptoteValue = forwardFriction.extremumValue = forwardFriction.asymptoteValue =
            Mathf.SmoothDamp(forwardFriction.asymptoteValue, handbrakeGripFactor * handBrakeFrictionMultiplier, ref frictionVelocity, driftSmoothFactor);

        for (int i = 0; i < 4; i++) {
            wheels [i].sidewaysFriction = sidewaysFriction;
            wheels [i].forwardFriction = forwardFriction;
        }
        sidewaysFriction.extremumValue = sidewaysFriction.asymptoteValue =forwardFriction.extremumValue = forwardFriction.asymptoteValue = 1.1f;
        for (int i = 0; i < 2; i++) {
            wheels [i].sidewaysFriction = sidewaysFriction;
            wheels [i].forwardFriction = forwardFriction;
        }
        rb.AddForce(transform.forward * (KPH /400)*10000);
    }
    else
    {
        sidewaysFriction = wheels[0].sidewaysFriction;
        forwardFriction = wheels[0].forwardFriction;

        forwardFriction.extremumValue = forwardFriction.asymptoteValue =
        sidewaysFriction.extremumValue = sidewaysFriction.asymptoteValue =
        ((KPH * handBrakeFrictionMultiplier) / 300) + 1;

        wheels[0].sidewaysFriction = sidewaysFriction;
        wheels[0].forwardFriction  = forwardFriction;
        wheels[1].sidewaysFriction = sidewaysFriction;
        wheels[1].forwardFriction  = forwardFriction;

        float steerFactor = Mathf.Abs(IM.horizontal);
        float speedFactor = Mathf.Clamp01((KPH - driftMinSpeed) / driftRampRange);
        float rearLoosen = driftIntensity * steerFactor * speedFactor;
        currentDriftAmount = driftIntensity > 0.0001f ? rearLoosen / driftIntensity : 0f;

        WheelFrictionCurve rearSideways = sidewaysFriction;
        rearSideways.extremumValue *= (1f - rearLoosen);
        rearSideways.asymptoteValue *= (1f - rearLoosen);

        wheels[2].sidewaysFriction = rearSideways;
        wheels[2].forwardFriction  = forwardFriction;
        wheels[3].sidewaysFriction = rearSideways;
        wheels[3].forwardFriction  = forwardFriction;

        if (rearLoosen > 0.01f)
        {
            rb.AddForce(transform.forward * rearLoosen * driftForceAssist);
        }
    }

    rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxSpinRate);
}
   
    // FIX: this is a coroutine now correctly started via StartCoroutine() in Awake(), only once
    private IEnumerator timedLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(.7f);
            radius = 6 +KPH /20;
        }
    }

}