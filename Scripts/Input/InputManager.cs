using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InputManager : MonoBehaviour
{
    public float vertical;
    public float horizontal;
    public bool handbrake;
    public bool boosting;

    [Header("Mobile Controls")]
    [Tooltip("Drag the Canvas/Panel holding your on-screen touch buttons here. It's auto-shown on Android and auto-hidden on PC/editor. Leave empty if you don't want auto show/hide.")]
    public GameObject mobileControlsPanel;
    [Tooltip("Force mobile controls on even in the editor, so you can test the on-screen buttons with a mouse without building to a device.")]
    public bool forceMobileControlsInEditor = false;

    // Set by the on-screen buttons via UI EventTrigger (PointerDown/PointerUp) calling the
    // Mobile...Down()/Mobile...Up() methods below. Combined with keyboard input every frame in
    // FixedUpdate so the same build works with keyboard on PC and touch buttons on Android.
    private bool mobileAccelerate;
    private bool mobileReverseBrake;
    private bool mobileLeft;
    private bool mobileRight;
    private bool mobileHandbrake;
    private bool mobileBoost;

    [Header("Control Mode")]
    [Tooltip("Manual reads real keyboard input, same as before. Automatic drives itself along the Waypoints path below - switch this at runtime from any other script, e.g. inputManager.controlMode = InputManager.ControlMode.Automatic;")]
    public ControlMode controlMode = ControlMode.Manual;

    public enum ControlMode
    {
        Manual,
        Automatic
    }

    [Header("Automatic - Orientation Markers (fixes reverse-driving bugs)")]
    [Tooltip("Empty child object placed at the FRONT of the car mesh (the nose). If the car's visual model isn't perfectly aligned with its root Transform's forward direction (very common after importing a car model), relying on transform.forward can make the AI misjudge which way it's actually facing and drive in reverse. Assign this + Back Marker to compute the car's TRUE facing direction instead. Leave both empty to fall back to transform.forward (old behavior).")]
    public Transform frontMarker;
    [Tooltip("Empty child object placed at the BACK of the car mesh (the tail). Used together with Front Marker.")]
    public Transform backMarker;

    // The AI's real facing direction, computed from the Front/Back markers if assigned, otherwise
    // falls back to transform.forward. ALL steering/scanning logic below uses this instead of
    // transform.forward directly, so fixing your car's marker placement fixes everything at once.
    private Vector3 CarForward
    {
        get
        {
            if (frontMarker != null && backMarker != null)
            {
                Vector3 dir = frontMarker.position - backMarker.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) return dir.normalized;
            }
            return transform.forward;
        }
    }

    // Similarly, use the midpoint between the markers as "where the car actually is" for distance
    // checks - slightly more accurate than the root Transform if it's offset from the visual center.
    // Falls back to transform.position if markers aren't assigned.
    private Vector3 CarPosition
    {
        get
        {
            if (frontMarker != null && backMarker != null)
                return (frontMarker.position + backMarker.position) * 0.5f;
            return transform.position;
        }
    }

    [Header("Automatic - Path")]
    [Tooltip("Tag every waypoint node (Node1, Node2, Node3...) with this exact Tag in the Inspector's Tag dropdown. At runtime the script finds all objects with this tag and orders them automatically by the number in their name - nothing to drag into any field.")]
    public string waypointTag = "Waypoints";

    [Tooltip("Read-only at runtime - shows what got auto-found via the tag, in the order the car will drive them, so you can confirm it picked up the right nodes in the right order.")]
    public GameObject[] waypoints;

    // UPGRADE: cached alongside `waypoints`, in the same order, so the per-frame corner-lookahead
    // scan doesn't call GetComponent<WaypointSpeedOverride>() up to cornerLookaheadNodes times every
    // single FixedUpdate. Populated once whenever LoadWaypointsFromTag() runs. Index i here always
    // corresponds to waypoints[i] (null entry = that node has no override).
    private WaypointSpeedOverride[] waypointSpeedOverrides;

    [Tooltip("How close (meters) the car needs to get to a waypoint before it targets the next one.")]
    public float waypointReachDistance = 6f;
    [Tooltip("If true, loops back to waypoint 0 after the last one (for repeated laps). If false, holds at the final waypoint.")]
    public bool loopWaypoints = true;

    // UPGRADE (cars converging onto the exact same line and clipping each other): AI used to steer
    // straight at each waypoint's exact position, so every car followed the literal same line. This
    // offsets the STEERING TARGET sideways (perpendicular to the path direction at that waypoint) by
    // a fixed amount per car, so each car follows the same road/spline shape but a lane's width apart
    // from the others - it never touches the raw waypoints[] positions used for corner-angle
    // detection below, only where the car aims.
    [Header("Automatic - Lane Offset")]
    [Tooltip("Sideways offset (meters) from the waypoint spline this car steers toward - positive = right of the path direction, negative = left, 0 = exactly on the spline (old behavior). RaceManager assigns each spawned car a different value via SetLaneOffset() so cars fan out into lanes instead of all following the identical line. Safe to leave at 0 for a single test car.")]
    public float laneOffset = 0f;
    [Tooltip("How many meters per second laneOffset is allowed to change when SetLaneOffset() smoothly retargets it. Set to 0 to snap instantly instead.")]
    public float laneOffsetChangeSpeed = 3f;

    private float targetLaneOffset = 0f;

    [Header("Automatic - Speed")]
    [Tooltip("Target speed (KPH) the automatic driver holds on straights - this is your 'how fast it completes the lap' knob.")]
    public float targetSpeed = 120f;
    [Tooltip("Target speed (KPH) the automatic driver slows to when approaching a sharp corner.")]
    public float cornerSpeed = 45f;
    [Tooltip("If the angle between the current and next path segment exceeds this many degrees, it's treated as a corner and the car slows toward cornerSpeed.")]
    public float cornerAngleThreshold = 25f;
    [Tooltip("How many waypoints AHEAD to scan for upcoming corners, not just the very next one. Higher = starts braking earlier for a corner that's still a few nodes away (useful for a sharp corner right after a long straight), and also lets the cumulative-angle hairpin check (see below) see the full arc of hairpins built from many closely-spaced nodes. 8-10 is a good starting point for tracks with dense hairpin clusters; 3-4 is fine for tracks with sparse, single-node corners.")]
    public int cornerLookaheadNodes = 4;
    [Tooltip("How hard to brake (0-1) when overspeeding into a corner. This is the BASE brake force - it now also gets scaled by how sharp the upcoming turn is, see the Angle-Based Braking section below.")]
    public float brakeStrength = 0.5f;
    [Tooltip("How many KPH of overspeed it takes to reach FULL brake strength. Braking now scales smoothly with how overspeed the car is instead of only kicking in past a fixed margin, so it actually slows down in time for corners instead of coasting into them.")]
    public float brakeSensitivityRange = 12f;
    [Tooltip("Multiplies targetSpeed and cornerSpeed - the quick 'difficulty' knob. 1 = as configured above, 1.2 = 20% faster overall, 0.8 = 20% slower. Easier than retuning every speed field per-car.")]
    public float speedMultiplier = 1f;

    [Header("Automatic - Angle-Based Braking")]
    // UPGRADE: previously braking force was a single flat value (brakeStrength) any time the car was
    // overspeeding into ANY corner, whether it was a lazy 26-degree kink or a full 90-degree hairpin -
    // both got treated basically the same. Now brake force scales smoothly with how sharp the sharpest
    // upcoming turn actually is (using the same lookahead scan already used for corner speed/hairpins),
    // between minBrakeAngle (light braking) and maxBrakeAngle (hard braking).
    [Tooltip("Turn angle (degrees) at/below which braking is at its gentlest (minAngleBrakeMultiplier) - e.g. a mild ~20-30 degree kink that barely needs the brakes.")]
    public float minBrakeAngle = 20f;
    [Tooltip("Turn angle (degrees) at/above which braking hits its hardest (maxAngleBrakeMultiplier) - e.g. a sharp ~90 degree corner or hairpin that needs a proper hard stop.")]
    public float maxBrakeAngle = 90f;
    [Tooltip("Brake force multiplier applied at minBrakeAngle - e.g. 0.3 = just a light tap of the brakes for a mild turn.")]
    public float minAngleBrakeMultiplier = 0.3f;
    [Tooltip("Brake force multiplier applied at maxBrakeAngle (or sharper) - e.g. 1.5 = extra hard braking for a sharp/hairpin turn, stacked on top of the normal overspeed-based braking.")]
    public float maxAngleBrakeMultiplier = 1.5f;

    [Header("Automatic - Sharp Corners / Hairpins")]
    [Tooltip("If the sharpest turn found during the corner lookahead scan exceeds this angle, it's treated as a hairpin - the AI brakes extra hard and steers extra aggressively so it actually completes the turn instead of running wide or plowing through it.")]
    public float hairpinAngleThreshold = 70f;
    [Tooltip("Extra speed reduction applied on top of cornerSpeed specifically for hairpin turns (e.g. 0.6 = drive the hairpin at 60% of the already-reduced corner speed).")]
    [Range(0.1f, 1f)] public float hairpinSpeedMultiplier = 0.65f;
    [Tooltip("Extra steering multiplier applied on top of steeringSensitivity while navigating a hairpin, so the car actually completes sharp turns instead of clipping the apex or running wide.")]
    public float hairpinSteerBoost = 1.5f;

    [Header("Automatic - Steering")]
    [Tooltip("Higher = sharper, more aggressive steering response toward the target waypoint.")]
    public float steeringSensitivity = 1.5f;
    [Tooltip("The angle (degrees) at which steering reaches full lock (before sensitivity/hairpin multipliers). Lower = the AI commits to full steering sooner, which helps it actually complete sharp turns instead of understeering through them.")]
    public float fullLockAngle = 45f;

    [Header("Automatic - Sharp-Turn Recovery")]
    [Tooltip("If the angle between the car's heading and the target waypoint exceeds this (e.g. after a spin or a bad respawn), the AI stops trying to slowly arc back around - which usually just grinds it into whatever's in front of it - and instead reverses while steering into the turn to whip around quickly.")]
    public float sharpTurnRecoveryAngle = 110f;
    [Tooltip("Once the AI angle drops below THIS (lower than the entry angle above), the recovery maneuver is considered successful and normal driving resumes. Keeping this noticeably lower than sharpTurnRecoveryAngle prevents rapid flip-flopping between reverse and forward right at the boundary.")]
    public float recoveryExitAngle = 60f;
    [Tooltip("Hard cap - if the recovery maneuver hasn't resolved the angle within this many seconds (e.g. the car is blocked while reversing too), force it back to normal driving anyway rather than reversing forever.")]
    public float maxRecoveryDuration = 3f;
    [Tooltip("If reversing during recovery/escape turns the car the WRONG way (e.g. it swings further away from the track instead of back toward it), enable this to flip the steering direction used for those maneuvers. Try toggling this if recovery still doesn't resolve after the other fixes - the correct direction depends on your vehicle controller's reverse-steering behavior, which this script can't see directly.")]
    public bool invertRecoverySteering = false;
    private bool inRecoveryManeuver = false;
    private float recoveryTimer = 0f;
    private float lastRecoverySteerSign = 1f;

    [Tooltip("If a reverse-recovery attempt times out WITHOUT resolving the angle (car is likely wedged, not just briefly spun), how many times to try a forward escape pulse + another reverse attempt before giving up entirely.")]
    public int maxEscapeAttempts = 4;
    [Tooltip("Duration (seconds) of each forward escape pulse - a brief forward nudge steering the opposite way, like the first move of a three-point turn, meant to reposition a wedged car before trying to reverse-correct again.")]
    public float escapePulseDuration = 0.6f;
    private bool inForwardEscapePulse = false;
    private float escapePulseTimer = 0f;
    private int escapeAttemptCount = 0;

    [Header("Automatic - Stuck Detection")]
    [Tooltip("If the car's actual speed stays below this (KPH) for stuckDetectionTime seconds while it's trying to drive normally (not already recovering) AND it isn't slowing down for a legitimate reason (boxed in behind traffic, or braking hard for a real corner/hairpin), it's treated as physically stuck - wedged against a wall or another car. This triggers the same reverse-recovery/escape-pulse routine used for bad angles.")]
    public float stuckSpeedThreshold = 4f;
    [Tooltip("How many seconds of near-zero speed (with no legitimate reason for it) before the car is declared stuck.")]
    public float stuckDetectionTime = 1.0f;
    private float stuckTimer = 0f;

    // FIX (false-positive stuck detection -> car reverses off track): the stuck-timer check below
    // runs at the TOP of DriveAutomatic(), before this frame's corneringAhead (braking hard for a
    // real corner/hairpin) and boxedInBraking (intentionally easing off behind a car with no gap to
    // pass) are computed further down in the same method. With only raw speed to go on, the old check
    // couldn't tell "actually wedged against a wall" apart from "deliberately going slow for a good
    // reason" - so a car legitimately crawling through a hairpin, or sitting boxed in behind traffic
    // for 2+ seconds, got misread as stuck and fired the full reverse-recovery maneuver (hard reverse
    // + full-lock steering for up to maxRecoveryDuration seconds) right off the racing line into a
    // boundary - a sudden, full swing, not a gradual one, matching the reported symptom exactly.
    //
    // Fix: cache LAST frame's corneringAhead/boxedInBraking here (set at the bottom of
    // DriveAutomatic(), once this frame's values are known) and use them to gate stuck-timer
    // accumulation at the top of the NEXT frame. One frame of lag is irrelevant given
    // stuckDetectionTime is measured in whole seconds.
    private bool wasCorneringAhead = false;
    private bool wasBoxedInBraking = false;

    [Header("Automatic - Car Avoidance")]
    [Tooltip("How far ahead (meters) the AI scans for other cars.")]
    public float obstacleDetectionRange = 10f;
    [Tooltip("Angle (degrees) of the two side-scan rays either side of straight-ahead.")]
    public float sideScanAngle = 25f;
    [Tooltip("How strongly the AI steers away from a car detected to one side - keeps it from clipping a car it's passing.")]
    public float avoidSteerStrength = 0.6f;
    [Tooltip("How hard the AI eases off/brakes when a car ahead can't be passed (boxed in on both sides), scaled by how close it is.")]
    public float avoidBrakeStrength = 0.7f;
    [Tooltip("UPGRADE: restricts the obstacle-detection raycasts to only these layers. Without this, a raycast can hit track geometry, terrain, or scenery FIRST and report 'no car ahead' even though a car is actually further down the road behind that geometry - or the AI can react to walls as if they were traffic. Set this to whatever layer your car bodies are on (leave as Everything only if all your cars and track share no overlapping colliders on the same ray).")]
    public LayerMask obstacleLayerMask = ~0;

    [Header("Automatic - Overtaking")]
    [Tooltip("Once the AI picks a side to pass a car on, it commits to that side for at least this many seconds instead of flip-flopping left/right.")]
    public float overtakeCommitTime = 1.2f;
    [Tooltip("How much steering bias (on top of normal avoidance) the AI applies while actively committed to passing - higher = more decisive, wider passes.")]
    public float overtakeSteerMultiplier = 1.6f;
    [Tooltip("How much throttle the AI gives up while actively overtaking (0 = keeps full throttle, 1 = same as a full boxed-in brake). Kept low so a car mid-pass doesn't lose its nerve and get re-caught.")]
    [Range(0f, 1f)] public float overtakeThrottleReduction = 0.15f;

    [Header("Automatic - Boost")]
    [Tooltip("If true, the AI holds boost whenever it's not slowing for a corner and not braking behind a boxed-in car - i.e. on clear straights, including mid-overtake.")]
    public bool useBoostOnStraights = true;

    [Header("Automatic - Competitive Catch-Up")]
    [Tooltip("If true, an AI car that's fallen behind the race leader gets a temporary speed boost (scales targetSpeed/cornerSpeed) proportional to how far behind it is. Without this, whichever car happens to fall behind early - a bad start, a bump, boxed in on lap 1 - just stays behind for the whole race, every race, no matter which prefab it is. This keeps every AI actively fighting for 1st instead of settling into a fixed pack order.")]
    public bool useCatchUpBoost = true;
    [Tooltip("How far behind the leader (in track-progress units - roughly 1.0 = one checkpoint segment's worth of distance) before catch-up starts kicking in at all. Being this close or closer to the leader gets no boost.")]
    public float catchUpStartGap = 1.5f;
    [Tooltip("Gap (same units as above) at which catch-up reaches full strength (catchUpMaxSpeedMultiplier). Falling even further behind than this doesn't add any more boost beyond the cap - keeps a badly-stuck car competitive without making it absurdly fast.")]
    public float catchUpMaxGap = 6f;
    [Tooltip("Speed multiplier applied on top of targetSpeed/cornerSpeed at FULL catch-up strength (i.e. at or beyond catchUpMaxGap behind). 1 = no boost, 1.25 = 25% faster.")]
    public float catchUpMaxSpeedMultiplier = 1.25f;
    [Tooltip("How often (seconds) to re-scan every car's progress to find the current leader and recompute this car's catch-up strength. Doesn't need to happen every single frame.")]
    public float catchUpRefreshInterval = 1f;

    [Header("Automatic - Difficulty Rubber-Band (vs. Player specifically)")]
    [Tooltip("If true, each AI's speed also nudges up or down based on its gap to the PLAYER specifically (separate from the leader catch-up above). AI ahead of the player eases off slightly; AI behind the player pushes slightly harder. This is what makes races feel adaptive to how well the player themself is doing, rather than just keeping the AI pack together.")]
    public bool useDifficultyRubberBand = true;
    [Tooltip("Gap to the player (same track-progress units as catch-up above) before difficulty adjustment starts kicking in at all.")]
    public float difficultyStartGap = 1f;
    [Tooltip("Gap to the player at which difficulty adjustment reaches full strength (difficultyMaxAdjustPercent). Further than this doesn't add any more adjustment.")]
    public float difficultyMaxGap = 5f;
    [Tooltip("Maximum speed adjustment, as a percent, applied at full strength. 8 = up to 8% faster when far behind the player, up to 8% slower when far ahead. Keep this small (a few %) - it's meant to be felt over a race, not seen as an obvious rubber band.")]
    [Range(0f, 25f)] public float difficultyMaxAdjustPercent = 8f;

    [Header("Automatic - Corner Exit Boost")]
    [Tooltip("If true, the AI fires boost once it's past the apex of a corner - i.e. once its current steering angle has unwound back down to cornerExitBoostAngleFraction of the sharpest angle it hit while turning in. This is what gives the 'slow in, fast out' feel: it overrides the normal 'no boost while cornering' rule for the exit half of the turn.")]
    public bool useCornerExitBoost = true;
    [Tooltip("Fraction (0-1) of the corner's peak turn-in angle the car must have straightened back out to before the exit boost fires. 0.5 = fires once it's unwound halfway out of the turn. Lower = fires later/deeper into the exit, higher = fires earlier.")]
    [Range(0.1f, 0.9f)] public float cornerExitBoostAngleFraction = 0.5f;
    [Tooltip("Minimum peak turn-in angle (degrees) a corner must have reached for it to qualify for an exit boost at all - filters out gentle kinks that shouldn't get a boost burst.")]
    public float cornerExitBoostMinPeakAngle = 30f;

    [Header("Automatic - AI Handbrake")]
    [Tooltip("If true, the AI taps the handbrake while turning INTO a sharp corner, like a real driver would, for a snappier rotation and a more dramatic feel. Automatically releases past the apex so it doesn't fight the corner-exit boost above.")]
    public bool useHandbrakeOnCorners = true;
    [Tooltip("The car's current steering angle (degrees, toward its target waypoint) must be at least this sharp before the AI will consider using the handbrake - keeps it from handbraking on mild kinks.")]
    public float handbrakeAngleThreshold = 55f;
    [Tooltip("Minimum speed (KPH) required before the AI will use the handbrake into a corner - no point handbraking while already slow.")]
    public float handbrakeMinSpeed = 40f;
    [Tooltip("Once the steering angle drops below this (degrees), the handbrake releases - i.e. this is 'far enough around the corner that it's time to let go and get back on the power'.")]
    public float handbrakeReleaseAngle = 20f;

    [Header("Debug")]
    [Tooltip("UPGRADE: draws the waypoint path, current target, hairpin nodes, and the three obstacle-scan rays in the Scene view (Play Mode) so you can tune all the numeric fields above by eye instead of guessing. Has zero effect on gameplay/builds - only visible in the editor's Scene view.")]
    public bool showDebugGizmos = true;

    private int currentWaypointIndex = 0;
    private controller vehicleController; // read-only reference, just for current KPH - controller.cs itself is untouched
    private float overtakeDir = 0f;    // -1 = passing on the left, +1 = passing on the right, 0 = not currently overtaking
    private float overtakeTimer = 0f;  // counts down while committed to overtakeDir

    // Tracks the sharpest |angleToTarget| seen since the car started turning in for the CURRENT
    // corner. Grows every frame while the car is still turning in deeper (angle increasing), then
    // stays fixed once the car starts straightening back out past the apex - so "current angle vs
    // this peak" tells us whether we're still entering or already exiting the turn. Resets to 0 once
    // the car is essentially straight again with no more corner ahead, ready for the next one.
    private float cornerPeakAngle = 0f;

    // Catch-up state: this car's own RaceProgress (for comparing track position against every other
    // car), the current speed multiplier that comes out of that comparison, and a timer so the
    // (relatively expensive) FindObjectsOfType scan only runs a few times a second, not every frame.
    private RaceProgress ownProgress;
    private float catchUpMultiplier = 1f;
    private float catchUpTimer = 0f;

    // Difficulty rubber-band state: cached reference to the player's RaceProgress (found once via
    // the "Player" tag rather than re-scanning every refresh - there's only ever one player car per
    // race, so no need to pay the FindObjectsOfType cost twice per refresh) and the resulting multiplier.
    private RaceProgress playerProgress;
    private float difficultyMultiplier = 1f;

    private void Awake()
    {
        vehicleController = GetComponent<controller>();
        ownProgress = GetComponent<RaceProgress>();
        LoadWaypointsFromTag();

        // Difficulty rubber-band only applies to AI cars comparing themselves to the player, so the
        // player's own car doesn't need (and shouldn't try) to find/compare against itself.
        if (ownProgress != null && !ownProgress.isPlayer)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerProgress = playerObj.GetComponent<RaceProgress>();
        }

        if (mobileControlsPanel != null)
        {
            bool showMobile = Application.platform == RuntimePlatform.Android
                            || Application.platform == RuntimePlatform.IPhonePlayer
                            || forceMobileControlsInEditor;
            mobileControlsPanel.SetActive(showMobile);
        }
    }

    // Finds every object tagged with waypointTag, then sorts them by the trailing number
    // in their name (Node1, Node2, Node10...) so the order is correct regardless of
    // Hierarchy order or the order Unity happens to return tagged objects in.
    private void LoadWaypointsFromTag()
    {
        if (string.IsNullOrEmpty(waypointTag))
        {
            Debug.LogWarning("InputManager: waypointTag is empty - can't find waypoints. Set it to the tag you gave your waypoint nodes (e.g. 'Waypoints').");
            return;
        }

        GameObject[] tagged = GameObject.FindGameObjectsWithTag(waypointTag);

        if (tagged == null || tagged.Length == 0)
        {
            Debug.LogWarning($"InputManager: no objects found with tag '{waypointTag}'. Check that your Node1, Node2... objects actually have this exact tag set in the Inspector, and that the tag exists under Project Settings > Tags and Layers.");
            waypoints = new GameObject[0];
            waypointSpeedOverrides = new WaypointSpeedOverride[0];
            return;
        }

        waypoints = tagged
            .OrderBy(go => ExtractTrailingNumber(go.name))
            .ToArray();

        // UPGRADE: cache each node's WaypointSpeedOverride (or null) once here instead of calling
        // GetComponent every frame from inside the corner-lookahead scan.
        waypointSpeedOverrides = waypoints
            .Select(go => go.GetComponent<WaypointSpeedOverride>())
            .ToArray();
    }

    // Pulls the trailing integer off a name like "Node1" -> 1, "Node23" -> 23.
    // Falls back to 0 if no trailing number is found, so oddly-named objects don't crash sorting.
    private static int ExtractTrailingNumber(string objectName)
    {
        Match match = Regex.Match(objectName, @"(\d+)\s*$");
        if (match.Success && int.TryParse(match.Value, out int result))
        {
            return result;
        }
        return 0;
    }

    // ── Presentation Toggle (Shift + A) ───────────────────────────────────
    // Only active on the player's car (root tagged "Player").
    // Lets you switch between Automatic (demo / hands-free) and Manual
    // (keyboard-driven) at any point during a race without touching the Inspector.
    //
    // Keybinds are exposed below so you can change them without recompiling.

    [Header("Presentation Mode Toggle (Player only)")]
    [Tooltip("Hold this modifier key while pressing toggleKey to flip between Automatic and Manual control. Default: Left Shift.")]
    public KeyCode toggleModifier = KeyCode.LeftShift;

    [Tooltip("Press this key while holding toggleModifier to switch control mode. Default: A.")]
    public KeyCode toggleKey = KeyCode.A;

    [Header("Scene Restriction")]
    [Tooltip("Driving input/AI logic only runs while the active scene matches the currently selected track (RaceSettings.SelectedTrackScene) - e.g. it stops in Showroom/garage and Score scenes, where there's no need to drive. Untick to disable the check entirely.")]
    public bool restrictToSelectedTrack = true;

    // Same scene-restriction pattern as controller.cs, so the two always agree on when a car
    // should actually be driving vs sitting idle in a non-race scene.
    private bool IsOutsideSelectedTrack()
    {
        return restrictToSelectedTrack && SceneManager.GetActiveScene().name != RaceSettings.SelectedTrackScene;
    }

    private void Update()
    {
        // Only the player car gets the live toggle - AI cars should never switch to Manual.
        if (!gameObject.CompareTag("Player")) return;

        // Don't process the Manual/Automatic toggle outside the race scene (e.g. Showroom/Score).
        if (IsOutsideSelectedTrack()) return;

        if (Input.GetKey(toggleModifier) && Input.GetKeyDown(toggleKey))
        {
            if (controlMode == ControlMode.Automatic)
            {
                controlMode = ControlMode.Manual;
                Debug.Log("[InputManager] Switched to MANUAL control (Shift+A pressed).");
            }
            else
            {
                controlMode = ControlMode.Automatic;
                // Reset recovery state so the AI picks up cleanly from wherever the car is.
                NotifyExternalRespawn();
                Debug.Log("[InputManager] Switched to AUTOMATIC control (Shift+A pressed).");
            }
        }
    }

    private void FixedUpdate()
    {
        // Outside the race scene (Showroom/garage, Score) there's no need to drive at all -
        // zero the input fields so controller.cs's own freeze doesn't fight against stale
        // vertical/horizontal/handbrake/boosting values, and skip AI waypoint logic entirely.
        if (IsOutsideSelectedTrack())
        {
            vertical = 0f;
            horizontal = 0f;
            handbrake = false;
            boosting = false;
            return;
        }

        if (controlMode == ControlMode.Manual)
        {
            // Keyboard/gamepad input - same as before, still works untouched on PC.
            float keyboardVertical = Input.GetAxis("Vertical");
            float keyboardHorizontal = Input.GetAxis("Horizontal");
            bool keyboardHandbrake = (Input.GetAxis("Jump") != 0) ? true : false;
            bool keyboardBoost = Input.GetKey(KeyCode.LeftShift);

            // Touch button input - set by MobileXxxDown()/MobileXxxUp() below, wired to the
            // on-screen buttons' EventTrigger PointerDown/PointerUp in the Inspector.
            float mobileVertical = 0f;
            if (mobileAccelerate) mobileVertical += 1f;
            if (mobileReverseBrake) mobileVertical -= 1f;

            float mobileHorizontal = 0f;
            if (mobileRight) mobileHorizontal += 1f;
            if (mobileLeft) mobileHorizontal -= 1f;

            // Combined so the exact same build works with keyboard on PC, touch buttons on
            // Android, or even both at once (e.g. testing in editor with mouse clicks).
            vertical = Mathf.Clamp(keyboardVertical + mobileVertical, -1f, 1f);
            horizontal = Mathf.Clamp(keyboardHorizontal + mobileHorizontal, -1f, 1f);
            handbrake = keyboardHandbrake || mobileHandbrake;
            boosting = keyboardBoost || mobileBoost;
        }
        else
        {
            DriveAutomatic();
        }
    }

    // ---- Mobile UI button hooks ----
    // Wire each of these to a button's EventTrigger component in the Inspector:
    // PointerDown -> the "...Down" method, PointerUp -> the matching "...Up" method.
    // (Use EventTrigger, not Button.onClick - onClick only fires on a full tap/release,
    // it can't tell you the button is being HELD, which is what driving needs.)
    public void MobileAccelerateDown() => mobileAccelerate = true;
    public void MobileAccelerateUp() => mobileAccelerate = false;

    public void MobileBrakeReverseDown() => mobileReverseBrake = true;
    public void MobileBrakeReverseUp() => mobileReverseBrake = false;

    public void MobileLeftDown() => mobileLeft = true;
    public void MobileLeftUp() => mobileLeft = false;

    public void MobileRightDown() => mobileRight = true;
    public void MobileRightUp() => mobileRight = false;

    public void MobileHandbrakeDown() => mobileHandbrake = true;
    public void MobileHandbrakeUp() => mobileHandbrake = false;

    public void MobileBoostDown() => mobileBoost = true;
    public void MobileBoostUp() => mobileBoost = false;

    // Scans every RaceProgress in the scene to find the current race leader's progress score, then
    // sets catchUpMultiplier based on how far behind THIS car is. Throttled by catchUpRefreshInterval
    // so it's not doing a full-scene scan every single physics frame for every AI car at once.
    private void UpdateCatchUpMultiplier()
    {
        if (!useCatchUpBoost || ownProgress == null)
        {
            catchUpMultiplier = 1f;
            return;
        }

        catchUpTimer -= Time.fixedDeltaTime;
        if (catchUpTimer > 0f) return; // reuse last computed multiplier until the next refresh
        catchUpTimer = catchUpRefreshInterval;

        float myScore = ownProgress.GetTrackProgressScore();
        float leaderScore = myScore;

        RaceProgress[] allProgress = FindObjectsOfType<RaceProgress>();
        for (int i = 0; i < allProgress.Length; i++)
        {
            if (allProgress[i] == null) continue;
            float score = allProgress[i].GetTrackProgressScore();
            if (score > leaderScore) leaderScore = score;
        }

        float gap = leaderScore - myScore; // 0 if this car IS the leader
        float t = Mathf.InverseLerp(catchUpStartGap, catchUpMaxGap, gap);
        catchUpMultiplier = Mathf.Lerp(1f, catchUpMaxSpeedMultiplier, t);
    }

    // Compares THIS car's progress against the player's specifically (not the leader) and nudges
    // speed up or down by a small percent either way. Shares the same refresh timer as catch-up above
    // since it's cheap once playerProgress is cached - no extra FindObjectsOfType scan needed.
    private void UpdateDifficultyMultiplier()
    {
        if (!useDifficultyRubberBand || ownProgress == null || ownProgress.isPlayer || playerProgress == null)
        {
            difficultyMultiplier = 1f;
            return;
        }

        float gap = ownProgress.GetTrackProgressScore() - playerProgress.GetTrackProgressScore();
        // gap > 0 => this AI is ahead of the player -> ease off. gap < 0 => AI is behind -> push harder.
        float t = Mathf.InverseLerp(difficultyStartGap, difficultyMaxGap, Mathf.Abs(gap));
        float adjust = (difficultyMaxAdjustPercent / 100f) * t;
        difficultyMultiplier = 1f + (gap > 0f ? -adjust : adjust);
    }

    // Produces the exact same vertical/horizontal/handbrake/boosting values a human would via input,
    // just computed from the waypoint path instead - controller.cs consumes them identically either way.
    private void DriveAutomatic()
    {
        UpdateCatchUpMultiplier();
        UpdateDifficultyMultiplier();

        if (waypoints == null || waypoints.Length == 0)
        {
            vertical = 0f;
            horizontal = 0f;
            handbrake = false;
            boosting = false;
            return;
        }

        float currentSpeed = (vehicleController != null) ? vehicleController.KPH : 0f;
        float recoverySignMultiplier = invertRecoverySteering ? -1f : 1f;

        // UPGRADE: ease laneOffset toward targetLaneOffset instead of only ever snapping, so a
        // mid-race SetLaneOffset(offset, snapInstantly:false) call doesn't yank the car sideways in
        // one frame. At spawn time RaceManager calls SetLaneOffset(offset) (snapInstantly=true by
        // default) so the very first frame already has the correct lane, not a lerp starting at 0.
        if (!Mathf.Approximately(laneOffset, targetLaneOffset))
        {
            laneOffset = (laneOffsetChangeSpeed <= 0f)
                ? targetLaneOffset
                : Mathf.MoveTowards(laneOffset, targetLaneOffset, laneOffsetChangeSpeed * Time.fixedDeltaTime);
        }

        Transform target = waypoints[currentWaypointIndex].transform;

        // UPGRADE: steer toward the waypoint's position shifted sideways by laneOffset, instead of
        // the exact waypoint position - this spreads cars into separate lanes along the same
        // road/spline instead of every car converging onto the identical line.
        Vector3 laneTargetPos = GetLaneOffsetPosition(currentWaypointIndex, laneOffset);
        Vector3 toTarget = laneTargetPos - CarPosition;
        toTarget.y = 0f;

        // Advance to the next waypoint once close enough. IMPORTANT: this distance check stays
        // against the REAL waypoint position (target.position), not the lane-offset position -
        // otherwise a large laneOffset could make the car "reach" a node early/late depending on
        // which side it's on, throwing off lap/progress tracking that keys off currentWaypointIndex
        // elsewhere (RaceProgress, corner-lookahead scanIndex, etc.).
        Vector3 toRealTarget = target.position - CarPosition;
        toRealTarget.y = 0f;

        if (toRealTarget.magnitude < waypointReachDistance)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= waypoints.Length)
            {
                currentWaypointIndex = loopWaypoints ? 0 : waypoints.Length - 1;
            }

            target = waypoints[currentWaypointIndex].transform;
            laneTargetPos = GetLaneOffsetPosition(currentWaypointIndex, laneOffset);
            toTarget = laneTargetPos - CarPosition;
            toTarget.y = 0f;
        }

        // Steering: signed angle between where the car ACTUALLY faces (per the Front/Back markers,
        // or transform.forward if those aren't assigned) and the direction to the target node.
        float angleToTarget = Vector3.SignedAngle(CarForward, toTarget.normalized, Vector3.up);

        // STUCK DETECTION: tracks how long the car has been going almost nowhere. This catches cases
        // the angle-only check misses - e.g. wedged against a wall at a moderate angle where the AI
        // keeps commanding full throttle but the car barely moves. Only accumulates while NOT already
        // in a recovery/escape maneuver (those have their own timers).
        //
        // FIX (false-positive stuck -> car reverses off track): also don't accumulate while LAST
        // frame's decision was a legitimate reason to be slow - boxed in behind traffic with no gap
        // to pass (boxedInBraking intentionally brakes toward near-zero speed), or actively braking
        // hard for a real upcoming corner/hairpin (corneringAhead). Those are normal, intended slow
        // driving, not "stuck" - the old check couldn't tell the difference and would misfire the
        // hard reverse-recovery maneuver right when the car was correctly crawling through a hairpin
        // or patiently following a boxed-in car, sending it off the racing line into a boundary.
        // wasCorneringAhead/wasBoxedInBraking are this frame's-worth-of-lag cache of those flags,
        // set at the bottom of this method once they're actually known for the current frame.
        if (!inRecoveryManeuver && !inForwardEscapePulse)
        {
            bool legitimatelySlow = wasCorneringAhead || wasBoxedInBraking;

            if (Mathf.Abs(currentSpeed) < stuckSpeedThreshold && !legitimatelySlow)
            {
                stuckTimer += Time.fixedDeltaTime;
            }
            else
            {
                stuckTimer = 0f;
            }
        }

        // FIX (AI feels stuck/dumb): if the car is facing almost the wrong way entirely - e.g. right
        // after a spin, a bump from another car, or a respawn - the old logic just tried to arc slowly
        // back toward the target, which in practice usually meant grinding forward into a wall or
        // another car for a couple seconds. Past sharpTurnRecoveryAngle we do a quick reverse-while-
        // steering-into-the-turn instead, like a real driver correcting a spin.
        //
        // Uses hysteresis + a hard timeout instead of re-checking the raw angle every single frame.
        // Once a recovery maneuver starts, it keeps reversing until EITHER the angle drops well below
        // the entry threshold (recoveryExitAngle, not the same number as sharpTurnRecoveryAngle - this
        // gap is what stops it flip-flopping in and out every frame) OR maxRecoveryDuration seconds
        // have passed, whichever comes first.
        //
        // The steer direction (lastRecoverySteerSign) is locked in ONCE, the moment the maneuver
        // starts, instead of being recalculated every frame from the constantly-shifting angle while
        // the car spins backward - recalculating every frame was the cause of a "stuck trying to
        // reverse-turn but never straightens out" bug, since the sign could flip mid-maneuver.
        //
        // Escape maneuver: a single reverse attempt can fail to resolve the angle if the car is
        // genuinely wedged (nose against a wall, side pinned in a corner) - reversing alone just pushes
        // it against whatever's already blocking it. When that happens (timed out AND still unresolved),
        // instead of giving up immediately, do a short FORWARD pulse steering the OPPOSITE way first -
        // like the first move of a real three-point turn - to actually reposition the car, then go back
        // into another reverse-recovery attempt. Repeats up to maxEscapeAttempts times before finally
        // giving up and resuming normal driving, so it can't get permanently wedged doing nothing.
        if (inForwardEscapePulse)
        {
            escapePulseTimer += Time.fixedDeltaTime;
            if (escapePulseTimer < escapePulseDuration)
            {
                vertical = 0.6f;
                horizontal = Mathf.Clamp(-lastRecoverySteerSign, -1f, 1f);
                handbrake = false;
                boosting = false;
                return;
            }

            inForwardEscapePulse = false;
            escapePulseTimer = 0f;
            // Falls through below - will re-evaluate the angle fresh and likely re-enter a normal
            // reverse-recovery attempt if it's still not resolved.
        }

        if (inRecoveryManeuver)
        {
            recoveryTimer += Time.fixedDeltaTime;
            bool angleResolved = Mathf.Abs(angleToTarget) < recoveryExitAngle;
            bool timedOut = recoveryTimer > maxRecoveryDuration;

            if (!angleResolved && !timedOut)
            {
                // Steer direction stays FIXED for the whole maneuver (set once below, at entry) -
                // this is the key fix for the oscillating/stuck-in-reverse bug.
                vertical = -1f;
                horizontal = Mathf.Clamp(lastRecoverySteerSign, -1f, 1f);
                handbrake = false;
                boosting = false;
                return;
            }

            inRecoveryManeuver = false;
            recoveryTimer = 0f;

            if (angleResolved)
            {
                escapeAttemptCount = 0; // resolved cleanly - reset the failure counter for next time
                stuckTimer = 0f;
            }
            else if (timedOut)
            {
                // Timed out without resolving - likely wedged. Try the escape pulse instead of just
                // giving up, unless we've already tried enough times.
                escapeAttemptCount++;
                if (escapeAttemptCount <= maxEscapeAttempts)
                {
                    inForwardEscapePulse = true;
                    escapePulseTimer = 0f;
                    vertical = 0.6f;
                    horizontal = Mathf.Clamp(-lastRecoverySteerSign, -1f, 1f);
                    handbrake = false;
                    boosting = false;
                    return;
                }
                else
                {
                    // Genuinely can't escape after several tries - stop trying and resume normal
                    // driving rather than looping forever. Reset the counters so it can try fresh again
                    // next time this happens somewhere else on the track.
                    escapeAttemptCount = 0;
                    stuckTimer = 0f;
                }
            }
        }
        else if (Mathf.Abs(angleToTarget) > sharpTurnRecoveryAngle || stuckTimer > stuckDetectionTime)
        {
            inRecoveryManeuver = true;
            recoveryTimer = 0f;
            stuckTimer = 0f;
            // Locked in for the whole maneuver - see note above. If the car sits at a near-zero
            // angle but is stuck (wedged straight-on into something), default to steering right.
            float sign = Mathf.Abs(angleToTarget) > 1f ? Mathf.Sign(angleToTarget) : 1f;
            lastRecoverySteerSign = sign * recoverySignMultiplier;
            vertical = -1f;
            horizontal = Mathf.Clamp(lastRecoverySteerSign, -1f, 1f);
            handbrake = false;
            boosting = false;
            return;
        }

        // Multi-node corner lookahead runs BEFORE steering is computed, so we know whether a hairpin
        // is coming up and can boost steering for it in the same pass. It scans several waypoints
        // ahead (cornerLookaheadNodes) and, if ANY of them needs a slower speed, starts braking toward
        // that now - even while still several nodes away from actually reaching it.
        //
        // UPGRADE: a WaypointSpeedOverride on a node no longer breaks the direction chain used to
        // detect the NEXT corner's sharpness. Previously, hitting an override node skipped computing
        // its outgoing direction entirely, so `incomingDir` went stale and the scan could misjudge the
        // sharpness of whatever corner came right after that override node. Now the turn-angle chain
        // always advances, and an explicit override simply takes priority over the auto-detected speed
        // for that specific node (a designer's explicit call for a node beats the automatic guess).
        //
        // UPGRADE: an override no longer blindly suppresses boost. Previously ANY override (even one
        // requesting a HIGHER speed, e.g. a "boost zone") set corneringAhead = true and killed boost.
        // Now boost is only suppressed when the required speed here is an actual slowdown relative to
        // the current cruising speed.
        //
        // FIX (hairpins built from many closely-spaced waypoints): the old code only tracked the
        // PER-SEGMENT turn angle (the angle between one pair of consecutive segments). That works
        // fine when a hairpin is represented by one node with a big angle change, but if a hairpin is
        // instead built from a dense cluster of nodes (e.g. 10+ nodes each only turning ~10-15
        // degrees), no single segment ever crosses cornerAngleThreshold/hairpinAngleThreshold even
        // though the CUMULATIVE turn across the cluster is a full hairpin - so the AI never slows
        // down or gets the hairpin steering boost, and just drives straight into the wall.
        //
        // Fix: also track the cumulative heading change from the START of the lookahead scan to each
        // point along it (scanStartDir -> incomingDir), and let sharpestTurnAngle be the max of the
        // per-segment angle AND this cumulative angle. This catches both a single sharp node AND a
        // hairpin spread across many gentle nodes, as long as cornerLookaheadNodes is large enough to
        // span the whole cluster (bump it up, e.g. 8-10, for tracks with dense hairpin waypoints).
        //
        // NOTE: this same scan also tracks sharpestTurnAngle across all the lookahead nodes, which is
        // now used TWICE further down - once for the existing hairpin speed/steer boost, and again for
        // the new angle-based brake force scaling (see the Angle-Based Braking section below).
        float desiredSpeed = targetSpeed * speedMultiplier;
        bool corneringAhead = false;
        bool isHairpin = false;
        float sharpestTurnAngle = 0f;

        Vector3 incomingDir = toTarget.normalized; // direction the car is currently heading toward its current target node
        Vector3 scanStartDir = incomingDir;        // NEW: heading at the start of the scan window, used for cumulative-angle detection
        int scanIndex = currentWaypointIndex;

        for (int i = 0; i < cornerLookaheadNodes; i++)
        {
            if (scanIndex >= waypoints.Length)
            {
                if (!loopWaypoints) break;
                scanIndex -= waypoints.Length;
            }

            int nextIdx = scanIndex + 1;
            bool hasNext = true;
            if (nextIdx >= waypoints.Length)
            {
                if (!loopWaypoints) hasNext = false;
                else nextIdx = 0;
            }

            float requiredSpeedHere = targetSpeed;
            bool restrictedHere = false;
            bool isSlowdownHere = false;

            if (hasNext)
            {
                Vector3 outgoing = waypoints[nextIdx].transform.position - waypoints[scanIndex].transform.position;
                outgoing.y = 0f;
                outgoing.Normalize();

                float turnAngle = Vector3.Angle(incomingDir, outgoing);
                sharpestTurnAngle = Mathf.Max(sharpestTurnAngle, turnAngle);

                if (turnAngle > cornerAngleThreshold)
                {
                    requiredSpeedHere = cornerSpeed;
                    restrictedHere = true;
                    isSlowdownHere = true;
                }

                // Chain forward unconditionally now, even on nodes with a speed override, so lookahead
                // stays accurate for corners beyond that node.
                incomingDir = outgoing;

                // NEW: cumulative turn from the start of the scan window to here. Catches a hairpin
                // that's spread across several closely-spaced nodes, where no individual segment above
                // crosses the threshold but the arc as a whole clearly is a hairpin.
                float cumulativeTurnAngle = Vector3.Angle(scanStartDir, incomingDir);
                sharpestTurnAngle = Mathf.Max(sharpestTurnAngle, cumulativeTurnAngle);

                if (cumulativeTurnAngle > cornerAngleThreshold)
                {
                    requiredSpeedHere = Mathf.Min(requiredSpeedHere, cornerSpeed);
                    restrictedHere = true;
                    isSlowdownHere = true;
                }
            }

            // An explicit per-node override takes priority over the auto-detected corner speed above.
            WaypointSpeedOverride speedOverride = (waypointSpeedOverrides != null && scanIndex < waypointSpeedOverrides.Length)
                ? waypointSpeedOverrides[scanIndex]
                : null;

            if (speedOverride != null)
            {
                requiredSpeedHere = speedOverride.overrideSpeedKph;
                restrictedHere = true;
                isSlowdownHere = speedOverride.overrideSpeedKph < (targetSpeed * speedMultiplier);
            }

            if (restrictedHere)
            {
                desiredSpeed = Mathf.Min(desiredSpeed, requiredSpeedHere * speedMultiplier);
                if (isSlowdownHere) corneringAhead = true;
            }

            if (!hasNext) break;
            scanIndex = nextIdx;
        }

        // Hairpin handling. A turn sharp enough to cross hairpinAngleThreshold gets slowed down even
        // further than a normal corner - this is what actually lets the AI complete a genuinely sharp
        // turn instead of carrying too much speed into it and running wide or spinning out.
        if (sharpestTurnAngle > hairpinAngleThreshold)
        {
            isHairpin = true;
            desiredSpeed *= hairpinSpeedMultiplier;
        }

        // Competitive catch-up: scale the whole speed target (straight or corner, whichever applies
        // above) by how far behind the leader this car currently is. A car right up at the front gets
        // catchUpMultiplier == 1 (no change) - this only ever speeds up cars that have fallen behind,
        // it never slows the leader down.
        desiredSpeed *= catchUpMultiplier;

        // Difficulty rubber-band: separately nudges speed based on THIS car's gap to the player
        // specifically, so the whole field's difficulty adapts to how the player is doing, not just
        // how the AI pack is doing relative to each other.
        desiredSpeed *= difficultyMultiplier;

        // Base steering toward the target waypoint, boosted further when a hairpin is ahead so the
        // car actually commits to and completes the turn instead of understeering through it.
        float steerScale = steeringSensitivity * (isHairpin ? hairpinSteerBoost : 1f);
        horizontal = Mathf.Clamp((angleToTarget / fullLockAngle) * steerScale, -1f, 1f);

        // CORNER PHASE TRACKING: angleToTarget (computed earlier from CarForward -> current target
        // waypoint) is a live, local measure of how hard the car is steering RIGHT NOW - unlike
        // sharpestTurnAngle above, which looks ahead at upcoming nodes. While the car is still turning
        // INTO a corner, this angle keeps growing; cornerPeakAngle latches onto that growing value.
        // Once the car starts coming back out of the corner, the live angle falls below the peak it
        // just set - that gap is exactly "how far past the apex are we", which both the exit-boost and
        // the handbrake logic below key off of.
        float absAngleToTarget = Mathf.Abs(angleToTarget);
        if (absAngleToTarget > cornerPeakAngle)
        {
            cornerPeakAngle = absAngleToTarget;
        }

        bool pastApex = cornerPeakAngle >= cornerExitBoostMinPeakAngle
                         && absAngleToTarget <= cornerPeakAngle * cornerExitBoostAngleFraction;

        // Corner fully cleared and nothing sharp coming up next - reset the peak so the NEXT corner
        // starts tracking fresh instead of inheriting this one's peak angle.
        if (absAngleToTarget < 5f && !corneringAhead)
        {
            cornerPeakAngle = 0f;
        }

        // AI HANDBRAKE: tap it while genuinely turning INTO a sharp corner at real speed - releases
        // automatically once the angle drops back below handbrakeReleaseAngle (i.e. past the apex and
        // straightening out), so it never fights the corner-exit boost below.
        handbrake = useHandbrakeOnCorners
                    && absAngleToTarget >= handbrakeAngleThreshold
                    && absAngleToTarget >= handbrakeReleaseAngle
                    && currentSpeed >= handbrakeMinSpeed;

        // Scan for other cars ahead and to each side and react - brake for a car directly in front
        // instead of ramming it, and steer away from a car alongside instead of drifting into it
        // mid-pass. This runs every frame on top of the waypoint steering above, so the AI still
        // generally follows the track while dodging traffic on it.
        Vector3 scanOrigin = CarPosition + Vector3.up * 0.5f;
        float centerDist = DistanceToOtherCar(scanOrigin, CarForward);
        float leftDist = DistanceToOtherCar(scanOrigin, Quaternion.Euler(0f, -sideScanAngle, 0f) * CarForward);
        float rightDist = DistanceToOtherCar(scanOrigin, Quaternion.Euler(0f, sideScanAngle, 0f) * CarForward);

        float avoidSteer = 0f;
        if (leftDist > 0f) avoidSteer += avoidSteerStrength * (1f - leftDist / obstacleDetectionRange);  // car to the left - steer right
        if (rightDist > 0f) avoidSteer -= avoidSteerStrength * (1f - rightDist / obstacleDetectionRange); // car to the right - steer left
        horizontal = Mathf.Clamp(horizontal + avoidSteer, -1f, 1f);

        // UPGRADE - ANGLE-BASED BRAKING: previously braking force used a single flat brakeStrength
        // any time the car was overspeeding for ANY corner, whether it was a lazy ~26 degree kink or a
        // full ~90 degree hairpin - both got the same brake force, just scaled by how overspeed you
        // were (via brakeSensitivityRange), not by how SHARP the turn actually is.
        //
        // Now we take sharpestTurnAngle (already computed above by the same lookahead scan used for
        // corner speed/hairpins) and map it onto a smooth 0-1 curve between minBrakeAngle and
        // maxBrakeAngle, then use that to blend between minAngleBrakeMultiplier (gentle brake for a
        // mild ~20-30 degree turn) and maxAngleBrakeMultiplier (hard brake for a sharp ~90 degree
        // corner). A 30-degree turn now brakes noticeably lighter than a 90-degree turn, instead of
        // both just hitting cornerSpeed and braking identically.
        float angleT = Mathf.InverseLerp(minBrakeAngle, maxBrakeAngle, sharpestTurnAngle);
        float angleBrakeMultiplier = Mathf.Lerp(minAngleBrakeMultiplier, maxAngleBrakeMultiplier, angleT);

        // Braking scales smoothly with how overspeed the car is, instead of only kicking in once
        // you're some fixed amount over. Starts braking proportionally as soon as it's above the
        // target, reaching full brakeStrength (further scaled by angleBrakeMultiplier above) once
        // it's brakeSensitivityRange kph over.
        float speedDiff = desiredSpeed - currentSpeed;
        if (speedDiff > 2f)
        {
            vertical = 1f; // meaningfully under target speed - accelerate
        }
        else if (speedDiff < -0.5f)
        {
            float overspeed = -speedDiff;
            float brakeFactor = Mathf.Clamp01(overspeed / brakeSensitivityRange);
            // FIX: brakeStrength * brakeFactor * angleBrakeMultiplier could exceed 1 in magnitude
            // (e.g. 4 * 1 * 2 = -8) once brakeStrength/angleBrakeMultiplier were tuned up - but
            // controller.cs's totalPower multiplies IM.vertical directly and expects it clamped to
            // -1..1 like every other input source (keyboard, mobile). An unclamped -8 sent a wildly
            // oversized reverse-torque spike through the wheels, locking/spinning them instead of
            // braking cleanly - which is exactly what was causing cars to lose grip and slide
            // straight into walls at corners instead of slowing down.
            vertical = Mathf.Clamp(-brakeStrength * brakeFactor * angleBrakeMultiplier, -1f, 1f);
        }
        else
        {
            vertical = 0f; // close enough to target speed, coast
        }

        // Actively decide whether the AI can pass a car ahead and commit to a side, only falling back
        // to braking when truly boxed in (a car on both sides at once) - keeps it from being passive
        // and easy to leave behind in traffic.
        // FIX (AI zig-zagging left-right during overtakes): centerDist only detects a car directly
        // AHEAD via a forward raycast. The moment the AI pulls alongside the car it's passing - which
        // is the entire point of an overtake - that car is no longer dead-ahead, so centerDist drops
        // to 0. The old code below fully wiped overtakeDir/overtakeTimer the instant that happened,
        // even mid-commitment, so the AI would lose its chosen passing side partway through the
        // maneuver and re-decide from scratch next frame - sometimes picking the OPPOSITE side,
        // which is exactly what produced the rapid left-right-left-right steering.
        //
        // Fix: treat "still mid-committed-pass" (overtakeDir set AND overtakeTimer still running) as
        // equally valid to "car currently detected ahead" - so a brief gap in forward detection while
        // alongside the other car no longer cancels the commitment early.
        bool stillCommittedToPass = overtakeDir != 0f && overtakeTimer > 0f;

        if (centerDist > 0f || stillCommittedToPass)
        {
            float closeness = 1f - Mathf.Clamp01(centerDist / obstacleDetectionRange);

            // Pick (or keep) a side to pass on. Committing for overtakeCommitTime seconds once chosen
            // stops the AI from flip-flopping steering direction every frame mid-pass.
            if (overtakeDir == 0f || overtakeTimer <= 0f)
            {
                bool leftBlocked = leftDist > 0f;
                bool rightBlocked = rightDist > 0f;

                if (!leftBlocked && !rightBlocked)
                {
                    // both sides open - pick whichever the car is already drifting toward, defaults right
                    overtakeDir = (angleToTarget < 0f) ? -1f : 1f;
                }
                else if (!leftBlocked) overtakeDir = -1f;
                else if (!rightBlocked) overtakeDir = 1f;
                else overtakeDir = 0f; // boxed in on both sides - no pass available right now

                overtakeTimer = overtakeCommitTime;
            }

            if (overtakeDir != 0f)
            {
                // FIX (car runs off track when passing): previously this applied FULL steering
                // strength (avoidSteerStrength * overtakeSteerMultiplier) the instant a car was
                // detected, even if it was still near the full obstacleDetectionRange away (e.g.
                // 50m). Stacked on top of normal spline-following steering, that's a very large
                // extra steering input applied way earlier than actually needed - easily enough to
                // push the car off the track, especially mid-corner, before it even needed to react.
                // Now the steering intensity scales with 'closeness' (already computed above, was
                // only being used for throttle) - gentle nudge when the other car is still far,
                // ramping up to full strength only once genuinely close alongside it.
                float steerRamp = Mathf.Lerp(0.25f, 1f, closeness);
                horizontal = Mathf.Clamp(horizontal + overtakeDir * avoidSteerStrength * overtakeSteerMultiplier * steerRamp, -1f, 1f);
                vertical = Mathf.Lerp(vertical, vertical * (1f - overtakeThrottleReduction), closeness);
            }
            else
            {
                // genuinely boxed in - follow safely behind until a gap opens
                vertical = Mathf.Lerp(vertical, -avoidBrakeStrength, closeness);
            }
        }
        else
        {
            // No car ahead AND no active commitment still running - fully safe to clear state.
            overtakeDir = 0f;
            overtakeTimer = 0f;
        }

        if (overtakeTimer > 0f) overtakeTimer -= Time.fixedDeltaTime;

        // handbrake is set above by the AI-handbrake corner logic (tied to the live angle-to-target)

        // AI holds boost whenever it isn't slowing for an upcoming corner and isn't stuck braking
        // behind a boxed-in car - so it uses it on straights and even mid-overtake to actually
        // complete the pass, same as a real opponent would. ALSO fires once the car is past the
        // apex of the corner it's currently exiting (pastApex, tracked above), even if corneringAhead
        // is still true because another corner follows soon after - this is the "fast out of the
        // turn" half of the slow-in/fast-out feel.
        bool boxedInBraking = centerDist > 0f && overtakeDir == 0f;
        bool cornerExitBoosting = useCornerExitBoost && pastApex;
        boosting = (useBoostOnStraights && !corneringAhead && !boxedInBraking) || (cornerExitBoosting && !boxedInBraking);

        // FIX (false-positive stuck detection): cache this frame's corneringAhead/boxedInBraking now
        // that they're known, so next frame's stuck-timer check (at the top of this method) can tell
        // "legitimately slow" apart from "actually stuck" instead of only looking at raw speed.
        wasCorneringAhead = corneringAhead;
        wasBoxedInBraking = boxedInBraking;
    }

    // Casts a ray from the AI car and reports the distance to the nearest OTHER car hit within
    // obstacleDetectionRange, or -1 if none. "Other car" = anything with a RaceProgress component
    // that isn't this car's own (so the AI never reacts to its own colliders).
    // UPGRADE: now uses obstacleLayerMask so the ray doesn't get blocked early by track/scenery
    // colliders that aren't cars, and doesn't waste a hit reacting to non-car layers at all.
    private float DistanceToOtherCar(Vector3 origin, Vector3 direction)
    {
        if (Physics.Raycast(origin, direction, out RaycastHit hit, obstacleDetectionRange, obstacleLayerMask))
        {
            RaceProgress otherProgress = hit.collider.GetComponentInParent<RaceProgress>();
            if (otherProgress != null && hit.transform.root != transform.root)
            {
                return hit.distance;
            }
        }
        return -1f;
    }

    // Convenience so another script (e.g. a race/AI manager) can force a re-scan and flip the mode in one call.
    public void SetAutomaticMode(bool enableAutomatic = true)
    {
        LoadWaypointsFromTag();
        currentWaypointIndex = 0;
        controlMode = enableAutomatic ? ControlMode.Automatic : ControlMode.Manual;
    }

    // ---- Hooks for VehicleRespawner (or any other external script) ----

    // UPGRADE: returns waypoints[index]'s position shifted sideways by offsetMeters, perpendicular
    // to the road's local direction at that node (not world X/Z), so the shift stays "a lane width
    // to the side of the road" consistently even through corners. Direction is taken from this node
    // to the next one (or from the previous node to this one, at the final non-looping node where
    // there is no "next"), matching the same direction the corner-lookahead scan already uses for
    // turn-angle geometry.
    private Vector3 GetLaneOffsetPosition(int index, float offsetMeters)
    {
        Vector3 basePos = waypoints[index].transform.position;
        if (Mathf.Approximately(offsetMeters, 0f) || waypoints.Length < 2) return basePos;

        int nextIdx = index + 1;
        Vector3 dir;
        if (nextIdx < waypoints.Length)
        {
            dir = waypoints[nextIdx].transform.position - basePos;
        }
        else if (loopWaypoints)
        {
            dir = waypoints[0].transform.position - basePos;
        }
        else
        {
            // Final node with no loop - fall back to the incoming direction from the previous node.
            dir = basePos - waypoints[index - 1].transform.position;
        }

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return basePos;
        dir.Normalize();

        // Vector3.Cross(dir, Vector3.up) points to the RIGHT of travel direction on the horizontal
        // plane - a positive laneOffset therefore means "this car's lane is to the right of center".
        Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
        return basePos + right * offsetMeters;
    }

    // Read-only view of which waypoint the AI currently considers its target, so a respawner can
    // teleport the car back to that node (and face it toward the next one) instead of guessing.
    public int CurrentWaypointIndex => currentWaypointIndex;

    // Lets an external script (e.g. RaceManager, right after spawning a car at a start point) find
    // which waypoint is closest to a given world position, so it can sync the AI's target index to
    // it via SetCurrentWaypointIndex below - otherwise a car spawned partway around the track would
    // default to targeting waypoint 0 and try to drive all the way back to the start of the path.
    // Returns 0 if there are no waypoints loaded yet.
    public int FindNearestWaypointIndex(Vector3 worldPosition)
    {
        if (waypoints == null || waypoints.Length == 0) return 0;

        int nearestIndex = 0;
        float nearestDistSqr = float.MaxValue;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;

            float distSqr = (waypoints[i].transform.position - worldPosition).sqrMagnitude;
            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    // Lets an external script (e.g. VehicleRespawner) re-sync the AI's target waypoint after
    // teleporting the car, so it immediately continues driving toward the correct next node instead
    // of first re-detecting its position on the path.
    public void SetCurrentWaypointIndex(int index)
    {
        if (waypoints != null && waypoints.Length > 0)
        {
            currentWaypointIndex = Mathf.Clamp(index, 0, waypoints.Length - 1);
        }
    }

    // UPGRADE: lets RaceManager give each spawned car its own sideways lane offset from the spline
    // (see laneOffset field comment above) right after Instantiate, so cars fan out into separate
    // lanes instead of all steering toward the identical center line. snapInstantly = true sets it
    // immediately (good for the very first assignment at spawn, before the car has moved at all);
    // false eases it in over laneOffsetChangeSpeed meters/sec, useful if you ever want to shift a
    // car's lane mid-race (e.g. for an overtake maneuver) without it snapping sideways in one frame.
    public void SetLaneOffset(float offset, bool snapInstantly = true)
    {
        targetLaneOffset = offset;
        if (snapInstantly) laneOffset = offset;
    }

    // Clears all in-progress recovery/escape/overtake state. Call this right after teleporting the
    // car (e.g. from VehicleRespawner) so the AI doesn't try to continue a reverse-recovery maneuver,
    // an escape pulse, or a mid-overtake commitment that no longer makes sense from the new position.
    public void NotifyExternalRespawn()
    {
        inRecoveryManeuver = false;
        inForwardEscapePulse = false;
        recoveryTimer = 0f;
        escapePulseTimer = 0f;
        escapeAttemptCount = 0;
        stuckTimer = 0f;
        overtakeDir = 0f;
        overtakeTimer = 0f;
        // FIX: also clear the stuck-detection legitimacy cache so a freshly-respawned car doesn't
        // inherit a stale "was cornering/boxed in" flag from wherever it used to be on the track.
        wasCorneringAhead = false;
        wasBoxedInBraking = false;
    }

    // UPGRADE: Scene-view debug visualization - draws the path, current target, hairpin nodes, and the
    // three obstacle-scan rays so all the numeric tuning fields above can be sanity-checked by eye.
    // Only runs in the editor's Scene view; has no effect on builds or actual gameplay.
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || waypoints == null || waypoints.Length == 0) return;

        // Path + node markers
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Vector3 pos = waypoints[i].transform.position;

            bool isCurrentTarget = Application.isPlaying && i == currentWaypointIndex;
            bool hasOverride = waypointSpeedOverrides != null && i < waypointSpeedOverrides.Length && waypointSpeedOverrides[i] != null;

            Gizmos.color = isCurrentTarget ? Color.green : (hasOverride ? Color.magenta : Color.yellow);
            Gizmos.DrawSphere(pos, isCurrentTarget ? 1.2f : 0.8f);

            int nextI = i + 1;
            if (nextI >= waypoints.Length)
            {
                if (!loopWaypoints) continue;
                nextI = 0;
            }
            if (waypoints[nextI] == null) continue;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pos, waypoints[nextI].transform.position);
        }

        if (!Application.isPlaying) return;

        // Obstacle-scan rays and current AI state, only meaningful once playing
        Vector3 scanOrigin = CarPosition + Vector3.up * 0.5f;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(scanOrigin, CarForward * obstacleDetectionRange);

        Gizmos.color = new Color(1f, 0.5f, 0f); // orange
        Gizmos.DrawRay(scanOrigin, (Quaternion.Euler(0f, -sideScanAngle, 0f) * CarForward) * obstacleDetectionRange);
        Gizmos.DrawRay(scanOrigin, (Quaternion.Euler(0f, sideScanAngle, 0f) * CarForward) * obstacleDetectionRange);

        // Small indicator above the car showing which behavior state it's currently in
        Color stateColor = Color.green; // normal driving
        if (inRecoveryManeuver) stateColor = Color.red;
        else if (inForwardEscapePulse) stateColor = new Color(1f, 0f, 1f); // magenta
        else if (overtakeDir != 0f) stateColor = Color.yellow;

        Gizmos.color = stateColor;
        Gizmos.DrawSphere(CarPosition + Vector3.up * 2.5f, 0.4f);
    }
}