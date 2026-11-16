using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class CheckpointManager : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip checkpointSound;
    public AudioClip lapCompleteSound;

    public static CheckpointManager Instance;

    [Header("Checkpoints (in order, index 0 = start/finish line)")]
    public Transform[] checkpoints;

    [Header("Lap Settings")]
    public int totalLaps = 3;

    [Header("UI References")]
    public TMP_Text lapText;
    public TMP_Text checkpointText;
    public TMP_Text timerText;
    public TMP_Text positionText;
    public TMP_Text raceFinishedText;

    [Header("Scene Settings")]
    public string scoreSceneName = "Score";
    public float delayBeforeSceneLoad = 0.5f;

    void Awake()
    {
        Instance = this;

        // FIX: totalLaps used to be a fixed Inspector value only. Now the laps popup in the
        // Showroom scene writes the chosen lap count into RaceSettings.Laps before loading this
        // scene, so we pick that up here. If RaceSettings.Laps is 0 or less (e.g. you pressed Play
        // directly on the F1 scene without going through the Showroom), the Inspector value is kept
        // as a safe fallback instead of forcing a 0-lap race.
        if (RaceSettings.Laps > 0)
        {
            totalLaps = RaceSettings.Laps;
        }
    }

    void Start()
    {
        if (raceFinishedText != null)
            raceFinishedText.gameObject.SetActive(false);
    }

    // CHANGED: now also takes the player's average speed for this race (from RaceProgress),
    // so it can be saved alongside the final time for the Score screen's stats bar.
    public void FinishRace(float finalTotalTime, float averageSpeedKph)
    {
        // Snapshot every car's standing right now, before anything moves on - RaceResults is
        // what ScoreManager reads to build the 1st/2nd/3rd... ranking list.
        if (RaceManager.Instance != null) RaceManager.Instance.ComputeFinalStandings();

        GrantRaceReward(averageSpeedKph);

        if (raceFinishedText != null)
        {
            raceFinishedText.gameObject.SetActive(true);
            raceFinishedText.text = "Race Finished!\nTotal Time: " + FormatTime(finalTotalTime);
        }

        PlayerPrefs.SetFloat("FinalTime", finalTotalTime);
        PlayerPrefs.SetFloat("AvgSpeed", averageSpeedKph); // NEW: read by ScoreManager for the "AVG SPEED" stat

        // NEW: best time is tracked PER TRACK (key includes the track scene name), so racing
        // Track2 doesn't overwrite a best time you set on Track1. Only overwritten when the
        // new time is actually faster (or no best exists yet for this track).
        string bestTimeKey = "BestTime_" + RaceSettings.SelectedTrackScene;
        float previousBest = PlayerPrefs.GetFloat(bestTimeKey, -1f);
        if (previousBest < 0f || finalTotalTime < previousBest)
        {
            PlayerPrefs.SetFloat(bestTimeKey, finalTotalTime);
        }

        PlayerPrefs.Save();

        Invoke(nameof(GoToScoreScene), delayBeforeSceneLoad);
    }

    void GoToScoreScene() => LoadingScreenController.LoadScene(scoreSceneName);

    [Header("XP Reward")]
    [Tooltip("Flat XP everyone gets just for finishing the race, regardless of placement.")]
    public int baseFinishXP = 50;
    [Tooltip("Extra XP for average speed: xpBonus = round(averageSpeedKph * this). Faster laps = more XP.")]
    public float xpPerAverageKph = 0.8f;
    [Tooltip("Coins awarded alongside XP (not shown in the Showroom HUD anymore, but still tracked).")]
    public int baseFinishCoins = 20;
    [Tooltip("Scales the FINAL xp total (base + placement + speed bonus) down before it's granted. " +
             "0.5 = half XP overall, 1 = no change. Kept separate from the individual XP values above " +
             "so their tooltips/relative weighting stay accurate - this just throttles the total.")]
    [Range(0.1f, 1f)]
    public float xpGainMultiplier = 0.5f;

    // Placement bonus - index 0 = 1st place. Anything beyond the array length reuses the last value.
    private static readonly int[] placementBonusXP = { 150, 100, 70, 40, 20 };

    // Computes and grants XP/coins the instant the player finishes, based on how well
    // AND how fast they raced. Also stashes the numbers in RaceReward so the Score
    // scene can show "+XXX XP" without recalculating anything.
    private void GrantRaceReward(float averageSpeedKph)
    {
        int place = GetPlayerPlace();
        int placementBonus = place >= 1
            ? placementBonusXP[Mathf.Min(place - 1, placementBonusXP.Length - 1)]
            : 0;

        int speedBonus = Mathf.RoundToInt(averageSpeedKph * xpPerAverageKph);
        int xpGained = Mathf.RoundToInt((baseFinishXP + placementBonus + speedBonus) * xpGainMultiplier);
        int coinsGained = baseFinishCoins + Mathf.RoundToInt(speedBonus * 0.5f);

        PlayerEconomy.AddReward(coinsGained, xpGained);

        RaceReward.XPGained = xpGained;
        RaceReward.CoinsGained = coinsGained;
        RaceReward.BadgeNameAtFinish = PlayerProfile.CurrentBadgeName;
    }

    // Reads the player's finishing place from RaceResults (already sorted 1st -> last
    // by RaceManager.ComputeFinalStandings, which we call right before this).
    private int GetPlayerPlace()
    {
        var standings = RaceResults.FinalStandings;
        for (int i = 0; i < standings.Count; i++)
        {
            if (standings[i].isPlayer) return i + 1;
        }
        return -1;
    }

    public void UpdateLapUI(int lap)
    {
        if (lapText != null) lapText.text = "Lap: " + lap + " / " + totalLaps;
    }

    // CHANGED: now shows how many of this lap's checkpoints have actually been collected
    // (e.g. "Checkpoints: 2 / 5") rather than the raw next-checkpoint index, since checkpoints
    // are now a real collect-them-all requirement instead of a single finish-line trigger.
    public void UpdateCheckpointUI(int collected, int total)
    {
        if (checkpointText != null) checkpointText.text = "Checkpoints: " + collected + " / " + total;
    }

    public void UpdateTimerUI(float time)
    {
        if (timerText != null) timerText.text = FormatTime(time);
    }

    // Called every frame from RaceProgress.Update (player only) with the player's live position.
    public void UpdatePositionUI(int place, int totalCars)
    {
        if (positionText != null) positionText.text = OrdinalPlace(place) + " / " + totalCars;
    }

    string OrdinalPlace(int place)
    {
        int lastTwoDigits = place % 100;
        if (lastTwoDigits == 11 || lastTwoDigits == 12 || lastTwoDigits == 13) return place + "th";

        switch (place % 10)
        {
            case 1: return place + "st";
            case 2: return place + "nd";
            case 3: return place + "rd";
            default: return place + "th";
        }
    }

    string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int milliseconds = Mathf.FloorToInt((time * 100f) % 100f);
        return string.Format("{0:00}:{1:00}:{2:00}", minutes, seconds, milliseconds);
    }
}