using UnityEngine;

// Attach to every car - player and AI alike. Tracks THIS car's own checkpoint/lap
// progress independently, instead of sharing one global counter across all cars.
public class RaceProgress : MonoBehaviour
{
    [Tooltip("Set true only on the player's car (RaceManager does this at spawn time). Controls whether UI/scene-load events fire for this car.")]
    public bool isPlayer = false;

    [Tooltip("Label shown on the final ranking list - RaceManager sets this to 'You' for the player and 'AI 1'/'AI 2'/... for opponents at spawn time.")]
    public string displayName = "Car";

    [Tooltip("Optional - the exact point on the car used to measure track progress (e.g. an empty at the car's collider/mass center, or the front bumper). Leave empty to just use this GameObject's own transform.")]
    public Transform positionReference;

    // CHANGED: back to a real "collect all checkpoints" system. nextCheckpointIndex is the
    // checkpoint this car must hit NEXT, in order (1, 2, 3, ...). CheckpointManager.checkpoints
    // holds every checkpoint transform including index 0 (start/finish), so the number of
    // intermediate checkpoints to collect per lap is checkpoints.Length - 1. Only once every one
    // of them has been hit does crossing checkpoint 0 again complete the lap - see HitCheckpoint.
    public int nextCheckpointIndex = 1;
    public int currentLap = 1;
    public float currentLapTime = 0f;
    public float totalTime = 0f;
    public bool raceFinished = false;

    // NEW: average speed tracking, used for the "AVG SPEED" stat on the Score screen.
    // Time-weighted rather than a simple average of samples, so it stays accurate
    // even if the frame rate varies during the race.
    private controller carController;
    private float speedTimeSum = 0f;
    public float averageSpeedKph = 0f;

    // FIX (live ranking wrong / stuck on 1st or wrong place): this used to read
    // InputManager.CurrentWaypointIndex, but that counter is ONLY advanced inside
    // InputManager.DriveAutomatic() - which only ever runs for AI cars. The player drives with
    // controlMode == Manual, so DriveAutomatic() never runs for the player and their
    // CurrentWaypointIndex stayed frozen at whatever it was set to at spawn, for the entire race.
    // That made GetTrackProgressScore() for the player never actually increase within a lap, so
    // ranking against AI (who WERE advancing) was essentially random/wrong - showing 1st while
    // actually last, jumping around, etc.
    //
    // Fix: track waypoint progress independently here, for player AND AI alike, based on actual
    // world position each frame - not by borrowing InputManager's AI-only counter.
    private InputManager inputManager;

    // Index of the waypoint-segment this car is on THIS lap (see RefreshPathProgress).
    // Same "Waypoints" nodes InputManager uses; ranking does not use checkpoint index.
    private int furthestWaypointIndexThisLap = 0;

    [Tooltip("Meters of extra distance a nearby later waypoint (e.g. the other side of a hairpin) must beat the current segment by, per waypoint-index of jump, before progress is allowed to snap there. Higher = harder to skip a corner in ranking.")]
    public float progressIndexJumpPenalty = 0.75f;

    private void Start()
    {
        carController = GetComponent<controller>(); // NEW: needed to read KPH each frame for the average-speed stat
        inputManager = GetComponent<InputManager>();

        // FIX (bug report): lap text (and checkpoint/position/timer) used to stay blank/default
        // until the FIRST lap actually completed, because UpdateLapUI was only ever called from
        // inside CompleteLap(). Push the starting values to the HUD immediately so it reads
        // correctly (e.g. "Lap: 1 / 3") from the very first frame of the race.
        if (isPlayer && CheckpointManager.Instance != null)
        {
            var cm = CheckpointManager.Instance;
            cm.UpdateLapUI(currentLap);
            int checkpointCount = cm.checkpoints != null ? cm.checkpoints.Length : 0;
            cm.UpdateCheckpointUI(GetCollectedCount(cm), Mathf.Max(0, checkpointCount - 1));
            cm.UpdateTimerUI(currentLapTime);

            if (RaceManager.Instance != null)
            {
                int place = RaceManager.Instance.GetPlayerPosition(out int totalCars);
                cm.UpdatePositionUI(place, totalCars);
            }
        }
    }

    private void Update()
    {
        if (!raceFinished)
        {
            currentLapTime += Time.deltaTime;
            totalTime += Time.deltaTime;

            // NEW: accumulate speed-over-time so averageSpeedKph = speedTimeSum / totalTime
            // is always the correct running average, regardless of frame rate.
            if (carController != null)
            {
                speedTimeSum += carController.KPH * Time.deltaTime;
                if (totalTime > 0f) averageSpeedKph = speedTimeSum / totalTime;
            }

            // Snap this car's spline index to where it actually is on the AI waypoint path
            // (same nodes InputManager follows). Ranking reads that below via GetTrackProgressScore.
            RefreshPathProgress();

            if (isPlayer)
            {
                CheckpointManager.Instance.UpdateTimerUI(currentLapTime);

                if (RaceManager.Instance != null)
                {
                    int place = RaceManager.Instance.GetPlayerPosition(out int totalCars);
                    CheckpointManager.Instance.UpdatePositionUI(place, totalCars);
                }
            }
        }
    }

    // Picks the waypoint-segment this car is actually on (same "Waypoints"-tagged nodes
    // InputManager uses to drive AI).
    //
    // ROOT CAUSE of "1st on the minimap / on track, but HUD says 4th / 4":
    // the minimap draws MiniCar child transforms (real world position). Ranking used a
    // high-water waypoint index that only advanced if the car passed within a few metres
    // of the AI racing line. The player can lead on the tarmac while that index stays
    // near 0; every AI hugging the spline out-ranks them.
    //
    // Fix: each frame choose the closest polyline segment, with a cost penalty for jumping
    // many indices (so the far side of a hairpin that is spatially close does not steal
    // progress). Then allow the high-water mark to catch up forward to that segment.
    // Checkpoints stay a separate sequential system and are not used for live place.
    private void RefreshPathProgress()
    {
        if (inputManager == null || inputManager.waypoints == null || inputManager.waypoints.Length < 2) return;

        var waypoints = inputManager.waypoints;
        int n = waypoints.Length;
        Vector3 pos = GetProgressPosition();
        FlattenY(ref pos);

        int cur = Mathf.Clamp(furthestWaypointIndexThisLap, 0, n - 2);
        int bestSeg = cur;
        float bestCost = float.MaxValue;
        float penalty = Mathf.Max(0f, progressIndexJumpPenalty);

        for (int i = 0; i < n - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;

            Vector3 a = waypoints[i].transform.position;
            Vector3 b = waypoints[i + 1].transform.position;
            FlattenY(ref a);
            FlattenY(ref b);

            ProjectOnSegment(pos, a, b, out float t, out float dist);
            float cost = dist + penalty * Mathf.Abs(i - cur);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestSeg = i;
            }
        }

        // High-water: catch up if the car is genuinely further along the spline.
        // Do not walk backward here - a wide line or a brief off-track moment would
        // otherwise drop rank while the minimap still shows the car ahead.
        if (bestSeg > furthestWaypointIndexThisLap)
            furthestWaypointIndexThisLap = bestSeg;
    }

    // Live ranking score. Formula:
    //   currentLap * waypointCount + furthestWaypointIndexThisLap + segmentFraction[0..1]
    // RefreshPathProgress runs first so this matches world position (what the minimap shows),
    // not a stale "did we hug the AI spline" counter. RankCars() only compares this one float.
    public float GetTrackProgressScore()
    {
        if (inputManager != null && inputManager.waypoints != null && inputManager.waypoints.Length > 0)
        {
            RefreshPathProgress();
            return currentLap * inputManager.waypoints.Length
                 + furthestWaypointIndexThisLap
                 + GetSegmentFraction();
        }

        return currentLap; // no waypoints to fall back on - lap count is the best we can do
    }

    // Returns 0-1 progress through ONE lap (for minimap node interpolation), driven off the
    // same waypoint high-water mark + segment fraction as GetTrackProgressScore.
    public float GetProgress01()
    {
        if (inputManager != null && inputManager.waypoints != null && inputManager.waypoints.Length > 0)
        {
            float n = inputManager.waypoints.Length;
            return Mathf.Clamp01((furthestWaypointIndexThisLap + GetSegmentFraction()) / n);
        }
        return 0f;
    }

    private Vector3 GetProgressPosition()
    {
        return positionReference != null ? positionReference.position : transform.position;
    }

    // How far this car is along the segment from the furthest reached waypoint to the next,
    // clamped to [0,1]. Live position, so ranking stays correct even before the high-water
    // mark advances this frame.
    private float GetSegmentFraction()
    {
        if (inputManager == null || inputManager.waypoints == null) return 0f;
        var waypoints = inputManager.waypoints;
        int fromIdx = furthestWaypointIndexThisLap;
        int toIdx = fromIdx + 1;
        if (toIdx >= waypoints.Length) return 0f;
        if (waypoints[fromIdx] == null || waypoints[toIdx] == null) return 0f;

        Vector3 from = waypoints[fromIdx].transform.position;
        Vector3 to = waypoints[toIdx].transform.position;
        Vector3 pos = GetProgressPosition();
        FlattenY(ref from);
        FlattenY(ref to);
        FlattenY(ref pos);

        Vector3 along = to - from;
        float denom = along.sqrMagnitude;
        if (denom < 0.0001f) return 0f;

        return Mathf.Clamp01(Vector3.Dot(pos - from, along) / denom);
    }

    private static void FlattenY(ref Vector3 v)
    {
        v.y = 0f;
    }

    private static void ProjectOnSegment(Vector3 p, Vector3 a, Vector3 b, out float t, out float dist)
    {
        Vector3 ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom < 0.0001f)
        {
            t = 0f;
            dist = Vector3.Distance(p, a);
            return;
        }

        t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / denom);
        dist = Vector3.Distance(p, a + ab * t);
    }

    // CHANGED: simple "collect all checkpoints" race system.
    // - Checkpoints 1, 2, 3... must be hit IN ORDER. Hitting one out of order (or hitting one
    //   you've already collected this lap) is just ignored - the car keeps racing, no penalty,
    //   but it doesn't advance anything. This also makes shortcut/skip exploits pointless: you
    //   physically cannot finish a lap without having passed through every checkpoint.
    // - Checkpoint 0 (start/finish line) only completes the lap once every intermediate
    //   checkpoint (1..checkpoints.Length-1) has been collected. If it's hit early, it's ignored
    //   and the car just keeps going - same as driving over the line mid-lap on a real track.
    public void HitCheckpoint(int checkpointIndex)
    {
        if (raceFinished) return;

        var cm = CheckpointManager.Instance;
        if (cm == null || cm.checkpoints == null || cm.checkpoints.Length == 0) return;

        int checkpointCount = cm.checkpoints.Length; // includes index 0 (start/finish)

        if (checkpointIndex == 0)
        {
            // Only counts as a lap completion once every intermediate checkpoint has been
            // collected - i.e. nextCheckpointIndex has advanced all the way past the last one.
            if (checkpointCount > 1 && nextCheckpointIndex < checkpointCount) return;

            CompleteLap(cm);

            // Reset collection progress for the new lap.
            nextCheckpointIndex = 1;
            furthestWaypointIndexThisLap = 0;

            if (isPlayer) cm.UpdateCheckpointUI(GetCollectedCount(cm), checkpointCount - 1);
            return;
        }

        // Intermediate checkpoint: only accept it if it's the one we're actually expecting next.
        // Anything else (out of order, or already collected) is silently ignored.
        if (checkpointIndex != nextCheckpointIndex) return;

        nextCheckpointIndex++;

        if (isPlayer)
        {
            cm.UpdateCheckpointUI(GetCollectedCount(cm), checkpointCount - 1);

            if (cm.audioSource != null && cm.checkpointSound != null)
                cm.audioSource.PlayOneShot(cm.checkpointSound);
        }
    }

    // How many of this lap's intermediate checkpoints have been collected so far.
    private int GetCollectedCount(CheckpointManager cm)
    {
        return Mathf.Clamp(nextCheckpointIndex - 1, 0, Mathf.Max(0, cm.checkpoints.Length - 1));
    }

    private void CompleteLap(CheckpointManager cm)
    {
        currentLap++;
        currentLapTime = 0f;

        if (isPlayer && cm.audioSource != null && cm.lapCompleteSound != null)
            cm.audioSource.PlayOneShot(cm.lapCompleteSound);

        if (currentLap > cm.totalLaps)
        {
            raceFinished = true;
            if (isPlayer) cm.FinishRace(totalTime, averageSpeedKph); // only the player finishing loads the Score scene
        }

        if (isPlayer) cm.UpdateLapUI(Mathf.Min(currentLap, cm.totalLaps));
    }

    // Returns the transform of the last checkpoint this car actually collected (or the
    // start/finish line if none collected yet this lap).
    public Transform GetLastCheckpointTransform()
    {
        var cm = CheckpointManager.Instance;
        if (cm == null || cm.checkpoints == null || cm.checkpoints.Length == 0) return null;
        int lastCollected = Mathf.Clamp(nextCheckpointIndex - 1, 0, cm.checkpoints.Length - 1);
        return cm.checkpoints[lastCollected];
    }

    // Returns the transform of the checkpoint this car needs to hit next (wraps to the
    // start/finish line once every intermediate checkpoint has been collected).
    public Transform GetNextCheckpointTransform()
    {
        var cm = CheckpointManager.Instance;
        if (cm == null || cm.checkpoints == null || cm.checkpoints.Length == 0) return null;
        int idx = nextCheckpointIndex < cm.checkpoints.Length ? nextCheckpointIndex : 0;
        return cm.checkpoints[idx];
    }
}