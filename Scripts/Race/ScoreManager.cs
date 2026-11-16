using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [Header("Score UI")]
    public TextMeshProUGUI finalTimeText;
    public float textRevealDelay = 0.3f;

    [Header("Testing")]
    [Tooltip("Leave this OFF for real races. When enabled, the score screen will generate fake results if no standings are saved.")]
    public bool useRandomTestDataIfEmpty = false;

    [Header("Stats Bar (Best Time / Total Time / Avg Speed)")]
    public TextMeshProUGUI bestTimeText;
    public TextMeshProUGUI totalTimeText;
    public TextMeshProUGUI avgSpeedText;

    [Header("Invalid Race (player pressed END RACE instead of finishing)")]
    [Tooltip("Optional - enabled only when RaceResults.RaceEndedEarly is true. Put your 'RACE ENDED EARLY / INVALID' banner GameObject here.")]
    public GameObject invalidRaceBanner;
    [Tooltip("Optional - text on/near the banner, e.g. 'RACE ENDED EARLY - RESULT INVALID'.")]
    public TextMeshProUGUI invalidRaceText;
    public string invalidRaceMessage = "RACE ENDED EARLY - RESULT INVALID";

    [Header("XP Reward (reads from RaceReward, set by CheckpointManager.FinishRace)")]
    public TextMeshProUGUI xpGainedText;   // e.g. "+312 XP"
    public TextMeshProUGUI newBadgeText;   // optional - shows current badge name, e.g. "AMATEUR"
    public Image newBadgeIcon;             // shows the matching badge sprite next to the XP/badge text
    public TextMeshProUGUI xpToNextBadgeText; // e.g. "688 XP to PRO" or "MAX BADGE REACHED"

    [Header("Badge Sprites (same order as PlayerProfile.Tiers: ROOKIE, AMATEUR, PRO, ELITE, LEGEND)")]
    [Tooltip("Assign the same 5 sprites you used on ShowroomManager, in the same order.")]
    public Sprite[] badgeSprites;

    [Header("Track Caption (trophy row under ranking list)")]
    [Tooltip("Optional - shows 'RACE | <track name>' under the results list, matching the mockup.")]
    public TextMeshProUGUI trackCaptionText;
    [Tooltip("Optional manual override. Leave empty to automatically show the track the player actually selected in the Showroom (RaceSettings.SelectedTrackName).")]
    public string trackDisplayName = "";

    [Header("Scene Navigation")]
    public string raceSceneName = "Track1";
    public string mainMenuSceneName = "Showroom";

    [Header("Rank Badge Sprites")]
    [Tooltip("Assign your 10 wreath/number sprites here, in order: index 0 = 1st place, index 1 = 2nd, ... index 9 = 10th. Leave slots empty if you don't have a sprite for that rank - it'll fall back to plain RankText.")]
    public Sprite[] rankBadgeSprites = new Sprite[10];

    [Header("Ranking List")]
    public Transform rankingListParent;
    [Tooltip("Row prefab. Root needs child objects named exactly: RankChip (Image), RankText (TMP), DriverNameText (TMP), TimeText (TMP), RowBackground (Image, optional), PlayerGlowBorder (GameObject, optional).")]
    public GameObject rankingRowPrefab;
    public Color playerRowColor = Color.cyan;

    [Tooltip("Leave at 0 to show only the actual racers from this race. Any value above 0 adds fake rows, which is what caused the fixed 7-player scoreboard bug.")]
    public int minDisplayedRows = 0;

    [Header("Ranking Row Chip Colors")]
    public Color goldColor = new Color(0.85f, 0.65f, 0.13f);
    public Color silverColor = new Color(0.6f, 0.6f, 0.6f);
    public Color bronzeColor = new Color(0.6f, 0.35f, 0.15f);
    public Color chipDefaultColor = new Color(0.15f, 0.15f, 0.15f);
    public Color dnfChipColor = new Color(0.2f, 0.2f, 0.2f);
    public Color rowColorEven = new Color(0.08f, 0.08f, 0.08f);
    public Color rowColorOdd = new Color(0.14f, 0.14f, 0.14f);
    public Color dnfTextColor = new Color(0.5f, 0.5f, 0.5f);

    [Tooltip("Optional - shows just 'You finished: 2nd'. Not used in the new mockup layout, kept for backward compatibility.")]
    public TextMeshProUGUI playerPlaceText;

    [Header("Podium Setup")]
    public GameObject[] carPrefabs;
    public Transform podiumSpawnPoint;

    [Header("Camera Reveal")]
    public Transform cameraStartPoint;
    public Transform cameraEndPoint;
    public float cameraMoveDuration = 1.5f;

    [Header("Podium Spin")]
    public bool spinCar = true;
    public float spinSpeed = 20f;

    [Header("Podium Spotlight")]
    public Light podiumSpotlight;
    public float spotlightIntensity = 30f;
    public float spotlightFadeSpeed = 4f;

    [Header("Scene Lighting")]
    [Tooltip("Drag your scene's Directional Light here.")]
    public Light directionalLight;
    [Tooltip("Intensity applied to the Directional Light on Start. Adjust freely from the Inspector.")]
    public float directionalLightIntensity = 1.5f;

    [System.Serializable]
    public class ExtraLight
    {
        public string label = "Light";
        public Light light;
        [Tooltip("Intensity applied to this light on Start.")]
        public float intensity = 1f;
    }

    [Tooltip("Add as many extra lights as you want (point lights, spotlights, etc). Each has its own adjustable intensity, applied on Start.")]
    public ExtraLight[] extraLights;

    private GameObject spawnedCar;
    private int playerFinishPlace = -1;
    private float cachedFakeFinalTime = -1f;

    void Start()
    {
        if (finalTimeText != null)
            finalTimeText.gameObject.SetActive(false);

        if (playerPlaceText != null)
            playerPlaceText.gameObject.SetActive(false);

        if (podiumSpotlight != null)
            podiumSpotlight.intensity = 0f;

        ApplySceneLighting();

        ShowInvalidRaceBanner();
        SpawnSelectedCar();
        BuildRankingList();
        SetTrackCaption();
        ShowXPReward();
        StartCoroutine(RevealSequence());
    }

    // Shows the "ended early / invalid" banner when RaceManager.EndRace() was used instead
    // of the player actually crossing the finish line. Hidden (and does nothing) otherwise.
    void ShowInvalidRaceBanner()
    {
        bool invalid = RaceResults.RaceEndedEarly;

        if (invalidRaceBanner != null)
            invalidRaceBanner.SetActive(invalid);

        if (invalidRaceText != null)
        {
            invalidRaceText.gameObject.SetActive(invalid);
            if (invalid) invalidRaceText.text = invalidRaceMessage;
        }
    }

    void Update()
    {
        if (spinCar && spawnedCar != null)
        {
            spawnedCar.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }
    }

    // Applies the configured intensities to the Directional Light and any extra lights.
    // Call this again at runtime (e.g. from a settings menu / slider) if you want live adjustment.
    void ApplySceneLighting()
    {
        if (directionalLight != null)
            directionalLight.intensity = directionalLightIntensity;

        if (extraLights != null)
        {
            foreach (var entry in extraLights)
            {
                if (entry != null && entry.light != null)
                    entry.light.intensity = entry.intensity;
            }
        }
    }

    void SpawnSelectedCar()
    {
        if (carPrefabs == null || carPrefabs.Length == 0 || podiumSpawnPoint == null)
        {
            Debug.LogWarning("ScoreManager: carPrefabs or podiumSpawnPoint not set up.");
            return;
        }

        int selectedIndex = Mathf.Clamp(PlayerPrefs.GetInt("pointer", 0), 0, carPrefabs.Length - 1);
        spawnedCar = Instantiate(carPrefabs[selectedIndex], podiumSpawnPoint.position, podiumSpawnPoint.rotation, podiumSpawnPoint);
    }

    IEnumerator RevealSequence()
    {
        if (cameraStartPoint != null)
        {
            Camera.main.transform.position = cameraStartPoint.position;
            Camera.main.transform.rotation = cameraStartPoint.rotation;
        }

        StartCoroutine(FadeSpotlight());
        yield return MoveCamera();

        yield return new WaitForSeconds(textRevealDelay);
        ShowFinalTime();
    }

    IEnumerator FadeSpotlight()
    {
        if (podiumSpotlight == null) yield break;

        while (Mathf.Abs(podiumSpotlight.intensity - spotlightIntensity) > 0.01f)
        {
            podiumSpotlight.intensity = Mathf.MoveTowards(podiumSpotlight.intensity, spotlightIntensity, spotlightFadeSpeed * Time.deltaTime);
            yield return null;
        }
        podiumSpotlight.intensity = spotlightIntensity;
    }

    IEnumerator MoveCamera()
    {
        if (cameraStartPoint == null || cameraEndPoint == null)
        {
            Debug.LogWarning("ScoreManager: cameraStartPoint or cameraEndPoint not set up.");
            yield break;
        }

        Vector3 startPos = cameraStartPoint.position;
        Quaternion startRot = cameraStartPoint.rotation;
        Vector3 endPos = cameraEndPoint.position;
        Quaternion endRot = cameraEndPoint.rotation;

        float t = 0f;
        while (t < cameraMoveDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / cameraMoveDuration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);

            Camera.main.transform.position = Vector3.Lerp(startPos, endPos, eased);
            Camera.main.transform.rotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        Camera.main.transform.position = endPos;
        Camera.main.transform.rotation = endRot;
    }

    // Shown instead of any time/speed number that wasn't actually calculated by a completed
    // race - used for the stats bar on an early-ended (invalid) race, and for DNF/invalid
    // rows in the ranking list, instead of a fake "--:--:--" clock format.
    private const string NotCalculated = "-----";

    void ShowFinalTime()
    {
        if (finalTimeText != null)
        {
            if (RaceResults.RaceEndedEarly)
            {
                // Nothing was actually completed - don't show a time at all, real or fake.
                finalTimeText.text = NotCalculated;
            }
            else
            {
                bool noRealData = PlayerPrefs.GetFloat("FinalTime", -1f) < 0f;
                float finalTime;
                if (noRealData && useRandomTestDataIfEmpty)
                {
                    if (cachedFakeFinalTime < 0f) cachedFakeFinalTime = Random.Range(120f, 220f);
                    finalTime = cachedFakeFinalTime;
                }
                else
                {
                    finalTime = PlayerPrefs.GetFloat("FinalTime", 0f);
                }
                // Mockup shows just the number under a separate "YOUR TIME" label,
                // so we drop the "Your Time: " prefix here. Keep it if your label isn't in a separate TMP object.
                finalTimeText.text = FormatTime(finalTime);
            }
            finalTimeText.gameObject.SetActive(true);
        }

        if (playerPlaceText != null && playerFinishPlace > 0)
        {
            playerPlaceText.text = "You finished: " + OrdinalPlace(playerFinishPlace);
            playerPlaceText.gameObject.SetActive(true);
        }

        ShowStatsBar();
    }

    void ShowStatsBar()
    {
        // Early-ended race: none of these were actually calculated by a completed race,
        // so show the "-----" placeholder instead of a real or randomly-faked number.
        if (RaceResults.RaceEndedEarly)
        {
            if (bestTimeText != null) bestTimeText.text = NotCalculated;
            if (totalTimeText != null) totalTimeText.text = NotCalculated;
            if (avgSpeedText != null) avgSpeedText.text = NotCalculated;
            return;
        }

        bool noRealData = PlayerPrefs.GetFloat("FinalTime", -1f) < 0f;

        float finalTime, avgSpeed, bestTime;

        if (noRealData && useRandomTestDataIfEmpty)
        {
            if (cachedFakeFinalTime < 0f) cachedFakeFinalTime = Random.Range(120f, 220f);
            finalTime = cachedFakeFinalTime;
            avgSpeed = Random.Range(80f, 160f);
            bestTime = finalTime - Random.Range(5f, 40f);
        }
        else
        {
            finalTime = PlayerPrefs.GetFloat("FinalTime", 0f);
            avgSpeed = PlayerPrefs.GetFloat("AvgSpeed", 0f);

            string bestTimeKey = "BestTime_" + RaceSettings.SelectedTrackScene;
            bestTime = PlayerPrefs.GetFloat(bestTimeKey, finalTime);
        }

        if (bestTimeText != null) bestTimeText.text = FormatTime(bestTime);
        if (totalTimeText != null) totalTimeText.text = FormatTime(finalTime);
        if (avgSpeedText != null) avgSpeedText.text = Mathf.RoundToInt(avgSpeed) + " KM/H";
    }

    // NEW: fills the "RACE | CITY CIRCUIT" caption under the ranking list.
    void SetTrackCaption()
    {
        if (trackCaptionText == null) return;

        // Priority: manual Inspector override (if you typed one in) > the track the player
        // actually picked in the Showroom (persisted via RaceSettings.SelectedTrackName) >
        // raw scene name as a last-resort fallback.
        string name = !string.IsNullOrEmpty(trackDisplayName)
            ? trackDisplayName
            : RaceSettings.SelectedTrackName;

        trackCaptionText.text = "RACE  |  " + name.ToUpper();
    }

    // Shows how much XP this specific race just earned, and the player's badge
    // AFTER that XP was applied (so a rank-up shows correctly on this screen).
    void ShowXPReward()
    {
        // RaceManager.EndRace() zeroes RaceReward before loading this scene, so an early-ended
        // race always shows "no reward" here rather than a stray "+0 XP" that reads like a
        // real (if unlucky) result.
        if (xpGainedText != null)
            xpGainedText.text = RaceResults.RaceEndedEarly ? "NO REWARD" : "+" + RaceReward.XPGained.ToString("N0") + " XP";

        string badgeName = string.IsNullOrEmpty(RaceReward.BadgeNameAtFinish)
            ? PlayerProfile.CurrentBadgeName
            : RaceReward.BadgeNameAtFinish;

        if (newBadgeText != null)
            newBadgeText.text = badgeName;

        if (newBadgeIcon != null)
        {
            int index = PlayerProfile.CurrentBadgeIndex;
            Sprite sprite = (badgeSprites != null && index >= 0 && index < badgeSprites.Length)
                ? badgeSprites[index]
                : null;
            newBadgeIcon.sprite = sprite;
            newBadgeIcon.enabled = sprite != null;
        }

        if (xpToNextBadgeText != null)
        {
            int index = PlayerProfile.CurrentBadgeIndex;
            bool maxTier = index >= PlayerProfile.Tiers.Length - 1;

            if (maxTier)
            {
                xpToNextBadgeText.text = "MAX BADGE REACHED";
            }
            else
            {
                int remaining = PlayerProfile.Tiers[index + 1].xpRequired - PlayerProfile.LifetimeXP;
                string nextTierName = PlayerProfile.Tiers[index + 1].name;
                xpToNextBadgeText.text = $"{remaining:N0} XP to {nextTierName}";
            }
        }
    }

    // UPDATED: builds rows for the new prefab layout - separate rank chip, name, time,
    // player highlighted via a glow border object instead of a full-row color fill,
    // DNF rows dimmed with "--:--:--" instead of a time.
    //
    // Row prefab must contain child objects named EXACTLY (case-sensitive):
    //   "RankChip"          -> Image (small chip behind the rank number)
    //   "RankText"          -> TextMeshProUGUI (child of RankChip, or anywhere on the row)
    //   "DriverNameText"    -> TextMeshProUGUI
    //   "TimeText"          -> TextMeshProUGUI
    //   "RowBackground"     -> Image (optional, faint alternating row fill)
    //   "PlayerGlowBorder"  -> GameObject (optional, cyan outline sprite, disabled by default)
    Transform DeepFind(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void BuildRankingList()
    {
        if (rankingListParent == null || rankingRowPrefab == null)
        {
            Debug.LogWarning("ScoreManager: rankingListParent or rankingRowPrefab not set up.");
            return;
        }

        foreach (Transform child in rankingListParent) Destroy(child.gameObject);

        bool invalid = RaceResults.RaceEndedEarly;

        var standings = RaceResults.FinalStandings;
        if ((standings == null || standings.Count == 0) && useRandomTestDataIfEmpty && !invalid)
            standings = GenerateFakeStandings();

        // When the player used END RACE instead of actually finishing, show the REAL
        // standings as they stood at that moment - real DNFs, real partial progress - rather
        // than dressing it up as a completed race. Faking times/padding the field only makes
        // sense for a genuine finish.
        if (!invalid)
        {
            // Only show the racers that actually took part in this race. We intentionally do NOT
            // pad the list to a fixed number such as 7, because that was the bug causing the
            // scoreboard to show more players than the showroom count selected by the user.
            standings = FillUnfinishedWithGeneratedTimes(standings);
        }

        int playerPlace = -1;

        for (int i = 0; i < standings.Count; i++)
        {
            var entry = standings[i];
            GameObject row = Instantiate(rankingRowPrefab, rankingListParent);

            // DeepFind works regardless of nesting depth
            Transform rankChipT   = DeepFind(row.transform, "RankChip");
            Transform rankBadgeT  = DeepFind(row.transform, "RankBadge");
            Transform rankTextT   = DeepFind(row.transform, "RankText");
            Transform driverNameT = DeepFind(row.transform, "DriverNameText");
            Transform timeTextT   = DeepFind(row.transform, "TimeText");
            Transform rowBgT      = DeepFind(row.transform, "RowBackground");
            Transform glowT       = DeepFind(row.transform, "PlayerGlowBorder");

            var rankChipImage  = rankChipT  != null ? rankChipT.GetComponent<UnityEngine.UI.Image>()              : null;
            var rankBadgeImage = rankBadgeT != null ? rankBadgeT.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
            var rankText       = rankTextT  != null ? rankTextT.GetComponent<TextMeshProUGUI>()                    : null;
            var nameText       = driverNameT!= null ? driverNameT.GetComponent<TextMeshProUGUI>()                  : null;
            var timeText       = timeTextT  != null ? timeTextT.GetComponent<TextMeshProUGUI>()                    : null;
            var rowBgImage     = rowBgT     != null ? rowBgT.GetComponent<UnityEngine.UI.Image>()                  : null;
            // PlayerGlowBorder now used as a rank-colored border on every row
            var glowImage      = glowT      != null ? glowT.GetComponentInChildren<UnityEngine.UI.Image>(true)      : null;

            bool isDNF = !entry.finished;
            // The player's own row gets called out as INVALID (not just DNF) whenever this
            // was an early-ended race - even if they happened to be at the finish line/last
            // checkpoint when they quit, the run still wasn't a real, complete result.
            bool isInvalidPlayerRow = invalid && entry.isPlayer;

            // Badge sprite (index 0 = 1st place)
            Sprite badgeSprite = null;
            if (!isDNF && !isInvalidPlayerRow && rankBadgeSprites != null && i < rankBadgeSprites.Length)
                badgeSprite = rankBadgeSprites[i];

            if (rankBadgeImage != null)
            {
                rankBadgeImage.sprite  = badgeSprite;
                rankBadgeImage.enabled = badgeSprite != null;
            }

            // RankText: plain number fallback, hidden when badge exists
            if (rankText != null)
            {
                bool showText = badgeSprite == null;
                rankText.gameObject.SetActive(showText);
                if (showText) rankText.text = isInvalidPlayerRow ? "INVALID" : (isDNF ? "DNF" : (i + 1).ToString());
            }

            if (nameText != null) nameText.text = entry.driverName.ToUpper();
            if (timeText != null) timeText.text  = (isInvalidPlayerRow || isDNF) ? NotCalculated : FormatTime(entry.totalTime);

            // RankChip: colored background only
            bool dimRow = isDNF || isInvalidPlayerRow;

            if (rankChipImage != null)
            {
                if      (dimRow) rankChipImage.color = dnfChipColor;
                else if (i == 0) rankChipImage.color = goldColor;
                else if (i == 1) rankChipImage.color = silverColor;
                else if (i == 2) rankChipImage.color = bronzeColor;
                else             rankChipImage.color = chipDefaultColor;
            }

            if (rowBgImage != null)
                rowBgImage.color = (i % 2 == 0) ? rowColorEven : rowColorOdd;

            if (dimRow)
            {
                if (rankText != null) rankText.color = dnfTextColor;
                if (nameText != null) nameText.color = dnfTextColor;
                if (timeText != null) timeText.color  = dnfTextColor;
            }

            bool isPlayer = entry.isPlayer;

            // Glow border is always ON now, tinted by rank (gold/silver/bronze/default),
            // player row overrides with playerRowColor - unless this is an invalid early-ended
            // run, in which case it stays dimmed like a DNF instead of celebrating a rank.
            if (glowT != null) glowT.gameObject.SetActive(true);
            if (glowImage != null)
            {
                if      (dimRow) glowImage.color = dnfChipColor;
                else if (i == 0) glowImage.color = goldColor;
                else if (i == 1) glowImage.color = silverColor;
                else if (i == 2) glowImage.color = bronzeColor;
                else             glowImage.color = chipDefaultColor;

                if (isPlayer && !isInvalidPlayerRow) glowImage.color = playerRowColor;
            }

            if (isPlayer && !isInvalidPlayerRow)
            {
                if (rankText != null) rankText.color = playerRowColor;
                if (nameText != null) nameText.color = playerRowColor;
                if (timeText != null) timeText.color  = playerRowColor;
            }

            if (isPlayer)
            {
                playerPlace = isInvalidPlayerRow ? -1 : i + 1;
            }
        }

        if (playerPlaceText != null && playerPlace > 0)
            playerFinishPlace = playerPlace;
    }

    // Generates a plausible finish time for every entry that hadn't actually crossed the line
    // yet (DNF at the moment the player finished), so the results screen always looks like a
    // completed race - no row is left showing "DNF" / "--:--:--".
    // Entries stay ordered by their existing rank (already sorted finished-first, then by track
    // progress); each still-racing car is given a time a little behind the one ranked above it,
    // scaled by how far around the track it had actually gotten so the numbers stay believable.
    // Always returns a NEW list - never modifies RaceResults.FinalStandings directly.
    List<RaceResults.Entry> FillUnfinishedWithGeneratedTimes(List<RaceResults.Entry> source)
    {
        var filled = new List<RaceResults.Entry>(source != null ? source : new List<RaceResults.Entry>());
        if (filled.Count == 0) return filled;

        // Baseline: the slowest real finishing time, or the player's own time as a fallback.
        float lastTime = PlayerPrefs.GetFloat("FinalTime", 120f);
        for (int i = 0; i < filled.Count; i++)
        {
            if (filled[i].finished && filled[i].totalTime > 0f)
                lastTime = filled[i].totalTime;
        }

        for (int i = 0; i < filled.Count; i++)
        {
            var entry = filled[i];
            if (entry.finished) continue;

            // Still racing when the player finished - space it 4-12 seconds behind whichever
            // time (real or already-generated) sits just above it, so the field stays ordered.
            lastTime += Random.Range(4f, 12f);

            entry.finished  = true;
            entry.totalTime = lastTime;
            filled[i] = entry;
        }

        return filled;
    }

    // Pads the standings list up to minDisplayedRows by appending fake AI entries
    // with realistic times just behind the last real finisher.
    // Always returns a NEW list - never modifies RaceResults.FinalStandings directly.
    List<RaceResults.Entry> PadStandings(List<RaceResults.Entry> source)
    {
        // Work on a fresh copy so we never mutate the real standings
        var padded = new List<RaceResults.Entry>(source != null ? source : new List<RaceResults.Entry>());

        if (padded.Count >= minDisplayedRows) return padded;

        // Find the slowest real finishing time to use as our starting baseline
        float lastTime = PlayerPrefs.GetFloat("FinalTime", 120f);
        for (int i = padded.Count - 1; i >= 0; i--)
        {
            if (padded[i].finished && padded[i].totalTime > 0f)
            {
                lastTime = padded[i].totalTime;
                break;
            }
        }

        // Build a set of names already used so we never duplicate
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < padded.Count; i++)
            if (!string.IsNullOrEmpty(padded[i].driverName))
                usedNames.Add(padded[i].driverName);

        int needed   = minDisplayedRows - padded.Count;
        int nameIdx  = 0;

        for (int i = 0; i < needed; i++)
        {
            // Pick the next AI name that isn't already in the list
            string name = "Racer " + (padded.Count + 1); // safe fallback
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string candidate = AIRacerNames.Get(nameIdx + attempt);
                if (!usedNames.Contains(candidate))
                {
                    name    = candidate;
                    nameIdx = nameIdx + attempt + 1;
                    break;
                }
            }
            usedNames.Add(name);

            // Space each fake entry 5-15 seconds behind the previous one
            lastTime += Random.Range(5f, 15f);

            var fakeEntry = new RaceResults.Entry
            {
                driverName     = name,
                isPlayer       = false,
                finished       = true,
                totalTime      = lastTime,
                lap            = 0,
                checkpointIndex = 0
            };
            padded.Add(fakeEntry);
        }

        return padded;
    }

    List<RaceResults.Entry> GenerateFakeStandings()
    {
        var list = new List<RaceResults.Entry>();
        float t = Random.Range(120f, 160f);

        int playerIndex = Random.Range(2, 8);
        int aiCount = 0;

        for (int i = 0; i < 10; i++)
        {
            bool isPlayer = (i == playerIndex);
            list.Add(new RaceResults.Entry
            {
                driverName = isPlayer ? PlayerProfile.Username : AIRacerNames.Get(aiCount++),
                isPlayer = isPlayer,
                finished = true,
                totalTime = t
            });
            t += Random.Range(3f, 8f);
        }
        return list;
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

    public void RestartRace()
    {
        RaceResults.Clear();
        LoadingScreenController.LoadScene(RaceSettings.SelectedTrackScene);
    }

    public void GoToMainMenu()
    {
        RaceResults.Clear();
        LoadingScreenController.LoadScene(mainMenuSceneName);
    }
}