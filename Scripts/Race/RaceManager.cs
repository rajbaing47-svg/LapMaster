using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class RaceManager : MonoBehaviour
{
    [Header("Vehicles")]
    [Tooltip("Same prefab list as the Showroom's vehicleList - order must match, since RaceSettings.SelectedVehicleIndex is just an index into this array.")]
    public vehicleList vehicles;

    [Header("Grid / Start Points")]
    [Tooltip("All start points, player's + every AI's, in ANY order - each race, one is randomly assigned to the player and the rest to AI, so who starts where changes race to race. Needs at least 2 (1 player + 1 AI) and ideally maxAICars + 1.")]
    public Transform[] startPoints;
    [Tooltip("Warns in the Console if any two startPoints are closer than this (meters) - two cars spawning this close will clip into each other and get stuck on start. Set to roughly one car length.")]
    public float minSpawnSeparation = 5f;

    // UPGRADE (cars spawning on top of each other): minSpawnSeparation above only ever produced a
    // Console warning - it never stopped two overlapping startPoints (or a startPoint sitting too
    // close to a car already placed this race) from actually spawning cars into each other. This
    // adds a real runtime fix: every car is now nudged clear of every car already spawned THIS race,
    // straight after Instantiate, before physics/AI ever gets a frame to react to the overlap.
    [Header("Grid / Overlap Auto-Nudge")]
    [Tooltip("Treated as each car's footprint radius (meters) for overlap checks. Two cars are considered overlapping if the distance between their spawn positions is less than carClearanceRadius * 2. Set to roughly half your car's length/width, whichever is larger.")]
    public float carClearanceRadius = 2.5f;
    [Tooltip("If a car's own startPoint is too close to an already-spawned car, it's nudged backward (opposite the startPoint's forward direction, i.e. further back on the grid) by this many meters per attempt.")]
    public float nudgeStepBack = 2f;
    [Tooltip("If a car's own startPoint is too close to an already-spawned car, it's also nudged sideways (along the startPoint's right/left) by this many meters per attempt, alternating sides, so cars fan out instead of forming a single line backward.")]
    public float nudgeStepSide = 1.5f;
    [Tooltip("Max nudge attempts before giving up and spawning at the last tried position anyway (logs a warning). Keeps this from looping forever if startPoints are badly laid out.")]
    public int maxNudgeAttempts = 6;

    // Positions of every car actually spawned so far THIS race - checked against for each new spawn.
    // Cleared implicitly each race since RaceManager lives on the track scene and is
    // destroyed/recreated on scene reload.
    private readonly List<Vector3> occupiedSpawnPositions = new List<Vector3>();

    // UPGRADE: real racers don't drive the exact spline center line - they each hold their own line,
    // a bit off center. Instead of every AI steering at the identical waypoint position (which is
    // what made cars converge onto the same line and clip into each other), each spawned car now
    // gets its own small, fixed sideways offset from the spline via InputManager.SetLaneOffset(). The
    // cars still follow the road/spline shape exactly (corners, curves, everything) - just shifted
    // sideways by a lane's width, like real traffic instead of single-file on a wire.
    [Header("Grid / Lane Offsets")]
    [Tooltip("How far apart (meters) each car's lane offset is from the next, alternating left/right of the spline - e.g. 1.5 gives offsets like -1.5, +1.5, -3, +3 for 4 cars. Keep this within your road's actual width (a couple offsets each side is usually plenty); too large and outer cars will run off the road edge on tight corners. Set to 0 to disable lane offsets and restore the old exact-spline-following behavior.")]
    public float laneOffsetSpread = 1.5f;

    // Running count of lane offsets assigned so far this race, used to alternate/grow the spread.
    private int laneOffsetAssignCount = 0;

    // Assigns the next spawned car (player or AI, whichever calls this next, in spawn order) an
    // offset that alternates side and grows outward every second car:
    // call 0 -> -spread, call 1 -> +spread, call 2 -> -2*spread, call 3 -> +2*spread...
    private float NextLaneOffset()
    {
        int n = laneOffsetAssignCount;
        laneOffsetAssignCount++;

        int magnitude = (n / 2) + 1;
        float sign = (n % 2 == 0) ? -1f : 1f;
        return sign * magnitude * laneOffsetSpread;
    }

    [Header("AI Opponents")]
    [Tooltip("Hard cap on opponents regardless of what RaceSettings.AICount says - matches the popup's max of 4.")]
    public int maxAICars = 4;
    [Tooltip("If true, the AI cars are never the same car the player picked. If false, the player's car is a valid random pick for AI too.")]
    public bool excludePlayerCarFromAI = true;

    [Header("Race Start")]
    public float countdownSeconds = 3f;
    [Tooltip("Drag a UI Text (or TextMeshProUGUI) here to show the 3, 2, 1, Go! countdown before the race starts. Leave empty to skip visuals and just use the timer.")]
    public TMPro.TextMeshProUGUI countdownText;

    [Header("Pause Panel Buttons")]
    [Tooltip("Scene to load for \"Go To Garage\". Matches the scene name used everywhere else in the project (ScoreManager.mainMenuSceneName, CarShowcaseIntroController.showroomSceneName).")]
    public string garageSceneName = "Showroom";
    [Tooltip("Scene to load for \"End Race\". Matches CheckpointManager.scoreSceneName so both a real finish and an early end land on the same Score scene.")]
    public string scoreSceneName = "Score";

    public static RaceManager Instance { get; private set; }
    public bool RaceStarted { get; private set; } = false;

    // FIX (wrong live rank / rank jumping to 8th, 9th etc.): RankCars() used to call
    // FindObjectsOfType<RaceProgress>() every frame, which pulls in ANY RaceProgress component
    // anywhere in the scene - showroom cars, pooled/inactive respawn copies, leftover prefab
    // instances - not just the cars actually in THIS race. Those stray entries got sorted in
    // alongside the real racers and could out-rank the player, making position jump around for
    // no visible reason. Fix: only ever rank the cars RaceManager itself spawned for this race,
    // recorded here at spawn time in SpawnPlayer/SpawnAI.
    private readonly List<RaceProgress> racers = new List<RaceProgress>();

    private void Awake()
    {
        // FIX: if there's more than one RaceManager in the scene (e.g. accidentally duplicated, or
        // one left over on a prefab), the LAST one's Awake() overwrites Instance - and if that second
        // one doesn't have its fields assigned, ValidateSetup fails silently on the one everyone is
        // actually reading from, while the FIRST one (which had valid fields) already spawned the cars.
        // That exactly matches "cars spawned fine, but RaceStarted never flips true."
        if (Instance != null && Instance != this)
        {
            Debug.LogError($"RaceManager: found MORE THAN ONE RaceManager in the scene! " +
                            $"'{Instance.gameObject.name}' was already registered, now '{gameObject.name}' is overwriting it. " +
                            $"Delete the extra RaceManager component/GameObject - only one should exist.");
        }
        Instance = this;

        Debug.Log($"RaceManager: Awake() running on '{gameObject.name}'.");

        if (!ValidateSetup())
        {
            // DEBUG: this is the #1 cause of "no car moves" - validation failed above, so cars never
            // get spawned and RaceStarted never becomes true, which freezes every car forever via
            // controller.cs's FreezeCar(). Fix the missing field(s) logged above in the Inspector.
            Debug.LogError("RaceManager: setup validation FAILED (see errors above). " +
                            "No cars will be spawned and the race will never start until this is fixed.");
            return;
        }

        Debug.Log("RaceManager: validation passed, spawning cars now...");

        List<Transform> grid = new List<Transform>(startPoints);
        Shuffle(grid); // FIX: randomize who starts where instead of always player-at-slot-0, AI-at-1,2,3...

        Transform playerSpawn = grid[0];
        int playerVehicleIndex = SpawnPlayer(playerSpawn);

        List<Transform> aiSpawns = grid.GetRange(1, grid.Count - 1);
        SpawnAI(playerVehicleIndex, aiSpawns);

        StartCoroutine(StartCountdown());
    }

    private bool ValidateSetup()
    {
        bool ok = true;
        if (vehicles == null || vehicles.vehicles == null || vehicles.vehicles.Length == 0)
        {
            Debug.LogError("RaceManager: vehicles (vehicleList) not assigned or empty.");
            ok = false;
        }
        if (startPoints == null || startPoints.Length < 2)
        {
            Debug.LogError("RaceManager: startPoints needs at least 2 entries (1 for the player, 1+ for AI).");
            ok = false;
        }
        else
        {
            // FIX (bug report): two cars spawning on/near the same point is exactly what caused
            // cars to spawn stuck inside each other. Check every pair up front and log precisely
            // which two transforms are too close, so it's a 30-second fix in the Inspector instead
            // of guessing which grid slot is the problem.
            for (int i = 0; i < startPoints.Length; i++)
            {
                if (startPoints[i] == null)
                {
                    Debug.LogError($"RaceManager: startPoints[{i}] is empty/unassigned.");
                    ok = false;
                    continue;
                }
                for (int j = i + 1; j < startPoints.Length; j++)
                {
                    if (startPoints[j] == null) continue;
                    float dist = Vector3.Distance(startPoints[i].position, startPoints[j].position);
                    if (dist < minSpawnSeparation)
                    {
                        Debug.LogError($"RaceManager: startPoints '{startPoints[i].name}' and '{startPoints[j].name}' " +
                                        $"are only {dist:F1}m apart (minSpawnSeparation is {minSpawnSeparation}m) - " +
                                        $"cars spawned here WILL overlap and get stuck. Move them further apart.");
                        ok = false;
                    }
                }
            }
        }
        return ok;
    }

    // UPGRADE (cars spawning on top of each other): starting from the startPoint's own
    // position/rotation, repeatedly checks against every car already spawned this race
    // (occupiedSpawnPositions) and nudges backward + alternating sideways until clear, or until
    // maxNudgeAttempts is hit. Cars stay ON the road (moved along the startPoint's own forward/right
    // axes, which live on the grid you placed, not on the waypoint spline) - this never touches
    // InputManager's waypoint-follow target, only where the car physically appears at t=0.
    private Vector3 ResolveSpawnPosition(Transform spawnPoint)
    {
        Vector3 candidate = spawnPoint.position;
        float minDist = carClearanceRadius * 2f;

        for (int attempt = 0; attempt < maxNudgeAttempts; attempt++)
        {
            bool overlapping = false;
            foreach (Vector3 occupied in occupiedSpawnPositions)
            {
                if (Vector3.Distance(candidate, occupied) < minDist)
                {
                    overlapping = true;
                    break;
                }
            }

            if (!overlapping)
            {
                if (attempt > 0)
                {
                    Debug.LogWarning($"RaceManager: startPoint '{spawnPoint.name}' was too close to another " +
                                      $"car's spawn - nudged clear after {attempt} attempt(s). Consider spacing " +
                                      $"startPoints further apart in the Inspector (>= {minDist:F1}m) to avoid this at runtime.");
                }
                return candidate;
            }

            // Step backward along the grid point's own forward direction (further back on the grid,
            // still on the road since it's relative to a point you placed there), and alternate
            // left/right each attempt so cars fan out instead of stacking in one line.
            int side = (attempt % 2 == 0) ? 1 : -1;
            candidate = spawnPoint.position
                        - spawnPoint.forward * (nudgeStepBack * (attempt + 1))
                        + spawnPoint.right * (nudgeStepSide * side * ((attempt / 2) + 1));
        }

        Debug.LogWarning($"RaceManager: startPoint '{spawnPoint.name}' still overlapping another car after " +
                          $"{maxNudgeAttempts} nudge attempts - spawning here anyway. Add more spacing between " +
                          $"startPoints or increase maxNudgeAttempts/nudgeStepBack.");
        return candidate;
    }

    // Returns the vehicle index used for the player, so SpawnAI can optionally exclude it.
    private int SpawnPlayer(Transform spawnPoint)
    {
        int index = Mathf.Clamp(RaceSettings.SelectedVehicleIndex, 0, vehicles.vehicles.Length - 1);
        GameObject playerPrefab = vehicles.vehicles[index];

        // UPGRADE: spawn at a nudged-clear position instead of blindly at spawnPoint.position - this
        // is what actually stops cars spawning on top of each other, on top of the fix already in
        // ValidateSetup() which only ever warned but never corrected anything.
        Vector3 spawnPos = ResolveSpawnPosition(spawnPoint);
        occupiedSpawnPositions.Add(spawnPos);

        GameObject playerCar = Instantiate(playerPrefab, spawnPos, spawnPoint.rotation);
        playerCar.tag = "Player";

        var rp = playerCar.GetComponent<RaceProgress>();
        if (rp != null)
        {
            rp.isPlayer = true;
            // Show the player's saved username in the standings list instead of "You".
            // PlayerProfile.Username is a static property backed by PlayerPrefs - always safe to read.
            rp.displayName = PlayerProfile.Username;
            racers.Add(rp); // register so RankCars() only ranks cars actually spawned for this race
        }
        else Debug.LogError($"RaceManager: '{playerPrefab.name}' is missing RaceProgress.");

        var im = playerCar.GetComponent<InputManager>();
        if (im != null)
        {
            im.controlMode = InputManager.ControlMode.Manual; // player drives with real input from race start
            // FIX (violent turn/crash at race start): sync the target waypoint to whichever node is
            // actually nearest THIS spawn point, instead of leaving it at InputManager's default of 0 -
            // see FindNearestWaypointIndex's comment in InputManager.cs for the full explanation.
            // UPGRADE: use spawnPos (post-nudge) rather than spawnPoint.position, since a nudged car
            // could otherwise sync to a waypoint index meant for its original, pre-nudge spot.
            im.SetCurrentWaypointIndex(im.FindNearestWaypointIndex(spawnPos));
            // UPGRADE: gives the player's own car a lane offset too, same as AI, so it isn't the one
            // car still aiming dead-center while every AI car has fanned out around it.
            im.SetLaneOffset(NextLaneOffset());
        }
        else Debug.LogError($"RaceManager: '{playerPrefab.name}' is missing InputManager.");

        ApplyShowroomDriveStats(playerCar, index);
        return index;
    }

    // Spawns RaceSettings.AICount cars (clamped to 1-maxAICars, and to however many spawn
    // points are actually available). Each one gets a randomly chosen prefab from the vehicle list.
    private void SpawnAI(int playerVehicleIndex, List<Transform> aiSpawnPoints)
    {
        int requestedCount = Mathf.Clamp(RaceSettings.AICount, 1, maxAICars);
        int count = Mathf.Min(requestedCount, aiSpawnPoints.Count);
        if (count < requestedCount)
        {
            Debug.LogWarning($"RaceManager: RaceSettings.AICount was {requestedCount} but only {aiSpawnPoints.Count} " +
                              $"spawn points are left after the player - spawning {count} AI car(s) instead. Add more startPoints to fix.");
        }

        List<int> pool = BuildVehiclePool(playerVehicleIndex);
        Shuffle(pool);

        for (int i = 0; i < count; i++)
        {
            // Walk the shuffled pool so AI cars don't repeat a model until every other
            // model has had a turn; only repeats if count > pool.Count (more opponents than cars).
            int vehicleIndex = pool[i % pool.Count];
            GameObject aiPrefab = vehicles.vehicles[vehicleIndex];

            Transform spawnPoint = aiSpawnPoints[i];

            // UPGRADE: same nudge-clear fix as SpawnPlayer - checks against the player's spawn AND
            // every AI car spawned so far this loop, so a 4th/5th AI car doesn't land on top of an
            // earlier one even if their startPoints were placed a bit too close together.
            Vector3 spawnPos = ResolveSpawnPosition(spawnPoint);
            occupiedSpawnPositions.Add(spawnPos);

            GameObject aiCar = Instantiate(aiPrefab, spawnPos, spawnPoint.rotation);
            aiCar.tag = "AICar";
            aiCar.name = $"{aiPrefab.name}_{AIRacerNames.Get(i)}";

            var im = aiCar.GetComponent<InputManager>();
            if (im != null)
            {
                im.controlMode = InputManager.ControlMode.Automatic;
                // FIX (violent turn/crash at race start): same fix as SpawnPlayer above - sync each
                // AI's target waypoint to whichever node is actually nearest ITS OWN spawn point,
                // instead of every AI car defaulting to node 0 regardless of which grid slot it got.
                // UPGRADE: uses spawnPos (post-nudge), same reasoning as SpawnPlayer.
                im.SetCurrentWaypointIndex(im.FindNearestWaypointIndex(spawnPos));

                // UPGRADE (cars driving single-file exactly on the spline all race, not just at
                // spawn): give each AI car its own fixed lane offset for the whole race, so they
                // spread across the road width like real traffic instead of chasing the identical
                // line. Alternates left/right and grows a bit further out every second car - never
                // all stacked on the same offset.
                im.SetLaneOffset(NextLaneOffset());
            }
            else Debug.LogError($"RaceManager: '{aiPrefab.name}' is missing InputManager.");

            var rp = aiCar.GetComponent<RaceProgress>();
            if (rp != null)
            {
                rp.displayName = AIRacerNames.Get(i);
                racers.Add(rp); // register so RankCars() only ranks cars actually spawned for this race
            }
            else Debug.LogError($"RaceManager: '{aiPrefab.name}' is missing RaceProgress.");

            ApplyShowroomDriveStats(aiCar, vehicleIndex);
        }
    }

    private static void ApplyShowroomDriveStats(GameObject car, int vehicleIndex)
    {
        if (!RaceSettings.TryGetCarDriveStats(vehicleIndex, out var stats)) return;

        var drive = car.GetComponent<controller>();
        if (drive == null)
        {
            Debug.LogError($"RaceManager: '{car.name}' is missing controller - cannot apply Showroom drive stats.");
            return;
        }

        drive.ApplyShowroomStats(stats.topSpeed, stats.acceleration, stats.handling, stats.braking);
    }

    // Builds the list of vehicle indices AI cars are allowed to randomly draw from.
    // Excludes the player's chosen car when excludePlayerCarFromAI is true and doing so
    // wouldn't leave the pool empty (a 1-car vehicleList always falls back to including it).
    private List<int> BuildVehiclePool(int playerVehicleIndex)
    {
        var pool = new List<int>();
        for (int i = 0; i < vehicles.vehicles.Length; i++)
        {
            if (excludePlayerCarFromAI && i == playerVehicleIndex) continue;
            pool.Add(i);
        }

        if (pool.Count == 0)
        {
            // Only one car in the whole list - AI has no choice but to reuse it.
            pool.Add(playerVehicleIndex);
        }

        return pool;
    }

    // Simple Fisher-Yates shuffle - used both for randomizing vehicle picks and now for
    // randomizing which start point each car gets.
    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Shared ranking logic - finished cars sorted by time, still-racing cars sorted by how far
    // around the track they've gotten. Used both for the live in-race position (GetPlayerPosition)
    // and the final results screen (ComputeFinalStandings), so the two always agree.
    private List<RaceProgress> RankCars()
    {
        // Only rank cars this RaceManager actually spawned (see 'racers' field comment above) -
        // skip any null entries too, in case a car was ever destroyed mid-race.
        var ranked = new List<RaceProgress>(racers.Count);
        for (int i = 0; i < racers.Count; i++)
        {
            if (racers[i] != null) ranked.Add(racers[i]);
        }

        ranked.Sort((a, b) =>
        {
            if (a.raceFinished != b.raceFinished) return a.raceFinished ? -1 : 1; // finished cars always rank above unfinished
            if (a.raceFinished) return a.totalTime.CompareTo(b.totalTime);         // both finished - faster time wins
            int progressCmp = b.GetTrackProgressScore().CompareTo(a.GetTrackProgressScore());
            if (progressCmp != 0) return progressCmp;
            // List.Sort is unstable - equal scores used to reshuffle every frame (RankCars runs
            // from the player's Update). InstanceID keeps a genuine tie in a consistent order.
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
        return ranked;
    }

    // Called every frame (via RaceProgress.Update, player only) while the race is in progress.
    // Returns the player's current place (1 = 1st) and how many cars are in the race total,
    // so the HUD can show "2 / 5" live, not just at the finish screen.
    public int GetPlayerPosition(out int totalCars)
    {
        var ranked = RankCars();
        totalCars = ranked.Count;

        for (int i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].isPlayer) return i + 1;
        }
        return -1; // shouldn't happen - no car flagged isPlayer
    }

    // Called by CheckpointManager.FinishRace() the instant the player crosses the line.
    // Snapshots every car's progress right then, in the same order GetPlayerPosition would show it.
    public void ComputeFinalStandings()
    {
        RaceResults.Clear();

        var ranked = RankCars();
        var entries = new List<RaceResults.Entry>(ranked.Count);

        foreach (var rp in ranked)
        {
            entries.Add(new RaceResults.Entry
            {
                driverName = rp.displayName,
                isPlayer = rp.isPlayer,
                finished = rp.raceFinished,
                totalTime = rp.totalTime,
                lap = rp.currentLap,
                checkpointIndex = rp.nextCheckpointIndex
            });
        }

        RaceResults.FinalStandings = entries;
    }

    private IEnumerator StartCountdown()
    {
        // FIX: added visible logs so you can actually confirm in the Console whether this countdown
        // is running and finishing, instead of guessing from the cars just sitting still.
        Debug.Log($"RaceManager: countdown started, race begins in {countdownSeconds} seconds...");

        // Shows "3, 2, 1, Go!" on countdownText (if assigned) during the countdown gap.
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);

            int secondsLeft = Mathf.CeilToInt(countdownSeconds);
            float tickLength = countdownSeconds / secondsLeft;

            for (int i = secondsLeft; i > 0; i--)
            {
                countdownText.text = i.ToString();
                yield return new WaitForSeconds(tickLength);
            }

            countdownText.text = "Go!";
            RaceStarted = true;
            Debug.Log("RaceManager: RaceStarted = true. Cars should move now.");

            yield return new WaitForSeconds(1f);
            countdownText.gameObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(countdownSeconds);
            RaceStarted = true;
            Debug.Log("RaceManager: RaceStarted = true. Cars should move now.");
        }
    }

    // ---------------------------------------------------------------------
    // Pause Panel Buttons
    // Hook these to the 3 buttons inside the pause Panel (the one PanelToggle
    // opens/closes on all 3 track scenes):
    //   RESTART RACE Button -> OnClick() -> RaceManager -> RestartRace()
    //   GO TO GARAGE Button -> OnClick() -> RaceManager -> GoToGarage()
    //   QUIT Button         -> OnClick() -> RaceManager -> QuitGame()
    // Since RaceManager is a singleton (Instance), you can also call these
    // from anywhere else via RaceManager.Instance.RestartRace() etc.
    // All three reset Time.timeScale first - the pause panel's PanelToggle
    // freezes time while open (pauseGameplay ticked), and that freeze would
    // otherwise carry over into whatever loads next / stay stuck at 0 if the
    // game were quit mid-pause.
    // ---------------------------------------------------------------------

    // Reloads whichever track scene is currently active, so this works
    // identically on Track1/Track2/Track3 without needing 3 separate buttons.
    public void RestartRace()
    {
        Time.timeScale = 1f;
        LoadingScreenController.LoadScene(SceneManager.GetActiveScene().name);
    }

    // Leaves the race and returns to the Showroom/garage.
    public void GoToGarage()
    {
        Time.timeScale = 1f;
        LoadingScreenController.LoadScene("First");
    }

    // Quits the application. Application.Quit() is a no-op inside the Editor,
    // so it's stopped separately there to actually exit Play Mode when testing.
    public void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------------------------------------------------------------
    // End Race Button
    // Hook this to an "END RACE" button (e.g. alongside RESTART/GARAGE/QUIT in the
    // pause Panel) -> OnClick() -> RaceManager -> EndRace().
    //
    // Unlike a real finish - which only happens when the player actually crosses the
    // line and CheckpointManager.FinishRace() runs - this lets the player bail out of
    // the race at any point and still be sent to the Score scene, with the run clearly
    // marked INVALID rather than a real result:
    //   - RaceResults.RaceEndedEarly is set so ScoreManager shows the real standings
    //     (including genuine DNFs) instead of faking finish times like it normally does.
    //   - No XP/coins are granted - GrantRaceReward() is never called, and any stale
    //     numbers from a previous real finish are cleared so the Score scene doesn't
    //     show a leftover "+XXX XP".
    //   - The player's own live stats (elapsed time, average speed) ARE saved, using the
    //     exact same PlayerPrefs keys CheckpointManager.FinishRace uses, so the Score
    //     scene's stats bar reflects the real partial run instead of stale numbers.
    // ---------------------------------------------------------------------
    public void EndRace()
    {
        Time.timeScale = 1f;

        // Snapshot every car's current standing with the same ranking logic used for a
        // real finish, so the Score scene still has a real list to show - it's just
        // marked invalid because nobody actually crossed the finish line.
        // IMPORTANT: this must run BEFORE the RaceEndedEarly flag is set below - it calls
        // RaceResults.Clear() internally, which also resets RaceEndedEarly to false. Setting
        // the flag first was the bug that made ScoreManager think this was a normal race.
        ComputeFinalStandings();

        RaceResults.RaceEndedEarly = true;

        // Save the player's own progress so far (same keys FinishRace uses) so the Score
        // scene's stats bar shows this run's real numbers, not leftovers from last time.
        RaceProgress playerProgress = racers.Find(r => r != null && r.isPlayer);
        if (playerProgress != null)
        {
            PlayerPrefs.SetFloat("FinalTime", playerProgress.totalTime);
            PlayerPrefs.SetFloat("AvgSpeed", playerProgress.averageSpeedKph);
            PlayerPrefs.Save();
        }

        // No reward for ending early - wipe any reward numbers left over from a previous
        // completed race so the Score scene doesn't display a stale/incorrect XP gain.
        RaceReward.XPGained = 0;
        RaceReward.CoinsGained = 0;

        LoadingScreenController.LoadScene(scoreSceneName);
    }
}