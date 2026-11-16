using UnityEngine;

// Attach this to the SAME GameObject as InputManager (and the car's Rigidbody/controller).
//
// Detects a car that's genuinely stuck - very low speed for stuckTimeToRespawn seconds WHILE
// actively trying to move (throttle pressed and/or boosting, matching "stuck even if boosting or
// speeding" from the request) - and teleports it back onto the track at the last waypoint it
// reached, facing toward the next waypoint in the path, with velocity zeroed out.
//
// This is a hard fallback that sits BEHIND InputManager's own reverse-recovery/escape-pulse logic.
// Use it for cases where a car is truly wedged and even those maneuvers can't free it - jammed
// between two other cars, clipped through geometry, flipped on its roof, launched off the track,
// etc. It works for both Automatic (AI) and Manual (player) controlMode, since it only looks at
// actual speed vs. actual input, not which mode is active.
public class VehicleRespawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The InputManager on this same car - used to read the waypoint path and the car's current target waypoint, and to reset the AI's in-progress recovery state after a respawn. Auto-found via GetComponent if left empty.")]
    public InputManager inputManager;
    [Tooltip("This car's Rigidbody - velocity/angular velocity are zeroed on respawn so it doesn't keep flying or spinning after being teleported. Auto-found via GetComponent if left empty.")]
    public Rigidbody rb;
    [Tooltip("This car's controller.cs - used to read current KPH to decide if it's actually moving. Auto-found via GetComponent if left empty.")]
    public controller vehicleController;

    [Header("Stuck Detection")]
    [Tooltip("Speed (KPH) below which the car is considered 'not really moving'.")]
    public float stuckSpeedThreshold = 4f;
    [Tooltip("How many seconds the car must stay below stuckSpeedThreshold WHILE actively trying to move before it gets respawned.")]
    public float stuckTimeToRespawn = 2f;
    [Tooltip("Minimum combined throttle/boost input magnitude required to count as 'actively trying to move'. Keeps a car that's simply parked/idle (no input at all) from being respawned - only a car that's flooring it and going nowhere counts as stuck.")]
    [Range(0f, 1f)] public float minTryingInput = 0.3f;

    [Header("Respawn Placement")]
    [Tooltip("Height (meters) added above the waypoint's Y position when placing the car, so it doesn't spawn clipped into the road mesh.")]
    public float respawnHeightOffset = 0.5f;
    [Tooltip("Seconds of grace immediately after a respawn during which stuck detection is suspended, giving the car time to get moving again before it can be judged stuck a second time.")]
    public float postRespawnGraceTime = 2f;

    [Header("Debug")]
    [Tooltip("Logs a message to the Console every time a car is respawned, including which waypoint it was sent back to.")]
    public bool logRespawns = true;

    private float stuckTimer = 0f;
    private float graceTimer = 0f;

    private void Awake()
    {
        if (inputManager == null) inputManager = GetComponent<InputManager>();
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (vehicleController == null) vehicleController = GetComponent<controller>();

        if (inputManager == null)
            Debug.LogWarning($"VehicleRespawner ({name}): no InputManager found on this GameObject - respawner can't find waypoints and will do nothing.");
        if (vehicleController == null)
            Debug.LogWarning($"VehicleRespawner ({name}): no controller found on this GameObject - respawner can't read speed and will do nothing.");
    }

    private void FixedUpdate()
    {
        if (inputManager == null || vehicleController == null) return;

        // FIX (cars teleport onto a waypoint right as the countdown ends): this never checked
        // whether the race had actually started. During the 3-2-1 countdown, controller.cs
        // intentionally freezes every car's Rigidbody velocity to zero (see FreezeCar()), but
        // InputManager.DriveAutomatic() keeps running regardless and still sets vertical=1 (full
        // throttle) toward the AI's target waypoint the whole time, since it has no idea the race
        // hasn't started either. That combination - "actively trying to move" (throttle=1) while
        // "speed stays ~0" (frozen by controller.cs) - is exactly this script's own stuck
        // definition, so after stuckTimeToRespawn seconds (which lines up suspiciously well with a
        // ~3 second countdown) it fired Respawn() and teleported every AI car onto its nearest
        // waypoint node right as the race began. Now stuck detection - and the stuckTimer itself -
        // is suspended entirely until RaceManager.Instance.RaceStarted is true, same gate
        // controller.cs already uses.
        if (RaceManager.Instance != null && !RaceManager.Instance.RaceStarted)
        {
            stuckTimer = 0f;
            return;
        }

        // Suspend detection for a short window right after a respawn so the car has a fair chance
        // to accelerate away before it could be flagged stuck again.
        if (graceTimer > 0f)
        {
            graceTimer -= Time.fixedDeltaTime;
            stuckTimer = 0f;
            return;
        }

        float speed = Mathf.Abs(vehicleController.KPH);

        // "Actively trying to move" = throttle pressed (forward OR reverse - a car stuck trying to
        // reverse out of a wedge counts too) or boosting. This is what makes it catch the "stuck even
        // while boosting/speeding" case instead of only flagging a car that's simply given up.
        float tryingInput = Mathf.Max(Mathf.Abs(inputManager.vertical), inputManager.boosting ? 1f : 0f);
        bool activelyTrying = tryingInput >= minTryingInput;

        if (activelyTrying && speed < stuckSpeedThreshold)
        {
            stuckTimer += Time.fixedDeltaTime;
        }
        else
        {
            stuckTimer = 0f;
        }

        if (stuckTimer >= stuckTimeToRespawn)
        {
            Respawn();
        }
    }

    // Teleports the car to the last waypoint it reached (InputManager.CurrentWaypointIndex), facing
    // toward the NEXT waypoint in the path, with velocity and angular velocity zeroed out.
    public void Respawn()
    {
        GameObject[] waypoints = inputManager.waypoints;
        if (waypoints == null || waypoints.Length == 0)
        {
            if (logRespawns) Debug.LogWarning($"VehicleRespawner ({name}): no waypoints available, can't respawn.");
            stuckTimer = 0f;
            return;
        }

        int index = Mathf.Clamp(inputManager.CurrentWaypointIndex, 0, waypoints.Length - 1);
        Transform node = waypoints[index].transform;

        int nextIndex = index + 1;
        if (nextIndex >= waypoints.Length)
        {
            nextIndex = inputManager.loopWaypoints ? 0 : index;
        }
        Transform nextNode = waypoints[nextIndex].transform;

        Vector3 facingDir = nextNode.position - node.position;
        facingDir.y = 0f;
        Quaternion facingRotation = facingDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facingDir.normalized, Vector3.up)
            : transform.rotation;

        Vector3 spawnPos = node.position + Vector3.up * respawnHeightOffset;

        if (rb != null)
        {
            // NOTE: if your Unity version is on the newer physics API (Unity 6 / 2023.3+), Rigidbody
            // no longer has `.velocity`/`.angularVelocity` - replace these two lines with
            // rb.linearVelocity and rb.angularVelocity stays the same name, only linear changed.
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = spawnPos;
            rb.rotation = facingRotation;
        }

        transform.position = spawnPos;
        transform.rotation = facingRotation;

        // Drop any in-progress reverse-recovery/escape-pulse/overtake state so the AI doesn't try to
        // continue a maneuver that no longer makes sense from the new position, and re-sync its
        // target waypoint index so it immediately continues toward the correct next node.
        inputManager.NotifyExternalRespawn();
        inputManager.SetCurrentWaypointIndex(index);

        graceTimer = postRespawnGraceTime;
        stuckTimer = 0f;

        if (logRespawns)
        {
            Debug.Log($"VehicleRespawner ({name}): stuck detected, respawned at waypoint '{node.name}' facing toward '{nextNode.name}'.");
        }
    }
}