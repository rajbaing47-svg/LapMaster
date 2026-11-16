using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

// Simplified scope: Track+Laps+Opponents panel, CarStatsPanel (speed/accel/handling/braking),
// Player/HUD panel (level, currency, settings), Race button, Back to Menu button, and the
// car carousel (Left/Right + page indicator). No paint or wheel customization.
public class ShowroomManager : MonoBehaviour
{
    [System.Serializable]
    public class CarSlot
    {
        [Header("Core")]
        public GameObject carObject;
        public Light spotlight;
        public Transform cameraViewpoint;

        [Header("Info Panel")]
        public string displayName = "CAR NAME";   // supports TMP rich text, e.g. "NEXUS <color=#3B9EFF>GT-R</color>"
        public string carClass = "S";              // shown in the small class badge

        [Header("Stats 0-10 scale - fill bars AND in-race controller (speed/accel/steer/brake)")]
        [Range(0, 10)] public float topSpeed = 8f;
        [Range(0, 10)] public float acceleration = 8f;
        [Range(0, 10)] public float handling = 8f;
        [Range(0, 10)] public float braking = 8f;

        [Header("Unlock")]
        [Tooltip("Index into PlayerProfile.Tiers required to race this car. 0 = ROOKIE = unlocked from the start. " +
                 "e.g. 2 means the player needs to have reached the SILVER badge (index 2) before they can race it.")]
        public int requiredBadgeIndex = 0;
    }

    [Header("Setup - one entry per car, in showroom order")]
    public CarSlot[] carSlots;

    [Header("Car Stats Panel")]
    public TMP_Text carNameText;
    public TMP_Text carClassText;
    public Image carRequiredBadgeIcon; // shows badgeSprites[carSlot.requiredBadgeIndex] - always visible, not just when locked
    public Image topSpeedFill;
    public Image accelerationFill;
    public Image handlingFill;
    public Image brakingFill;
    public TMP_Text topSpeedNumber;
    public TMP_Text accelerationNumber;
    public TMP_Text handlingNumber;
    public TMP_Text brakingNumber;

    [Header("Carousel Nav")]
    public TMP_Text pageText; // shows "2 / 3"

    [Header("Badge Sprites (shared - same order as PlayerProfile.Tiers)")]
    [Tooltip("5 sprites, in order: ROOKIE, BRONZE, SILVER, GOLD, LEGEND. Used for both the username panel badge icon and the locked-car popup's required-badge icon.")]
    public Sprite[] badgeSprites;

    [Header("Player/HUD Panel - reads from PlayerProfile (name + lifetime-XP badge tier)")]
    public TMP_Text usernameText;
    public TMP_Text badgeNameText;   // e.g. "ROOKIE" / "BRONZE" / "SILVER" ...
    public Image badgeIcon;          // shows badgeSprites[PlayerProfile.CurrentBadgeIndex]
    public Image xpBarFill;          // progress toward the NEXT badge tier
    public TMP_Text xpProgressText;  // e.g. "1,850 / 8,000"
    public Button settingsButton;    // wire to your own settings panel, not handled here

    [Header("Car Lock Message (optional - plain text, no icon needed)")]
    [Tooltip("Shown under the car name when the currently viewed car is locked. Leave empty if you don't want this.")]
    public TMP_Text lockMessageText;

    [Header("Locked Car Popup (shown while browsing a locked car, hides on switch)")]
    [Tooltip("A small panel GameObject, hidden by default, with a TMP text inside. Leave empty to skip this.")]
    public GameObject lockedPopup;
    public TMP_Text lockedPopupText;
    public Image lockedPopupBadgeIcon; // shows badgeSprites[requiredBadgeIndex] for the currently viewed car

    [Header("Race Setup Panel - always visible, not a popup")]
    public TMP_Text aiCountText;
    public TMP_Text lapsText;
    public int maxAICount = 4;
    public int minAICount = 1;
    public int maxLaps = 10;
    public int minLaps = 1;

    [Header("Track Selection")]
    public TrackOption[] trackOptions;
    public TMP_Text trackNameText;
    public TMP_Text trackSubtitleText; // e.g. "City Circuit"
    public Image trackPreviewImage;

    [System.Serializable]
    public class TrackOption
    {
        public string trackName;
        public string trackSubtitle;
        public string sceneName;
        public Sprite previewImage;
    }

    private int selectedAICount = 3;
    private int selectedLaps = 3;
    private int selectedTrackIndex = 0;

    [Header("Transition")]
    public float cameraTransitionDuration = 0.8f;
    public float spotlightFadeSpeed = 4f;
    public float activeSpotlightIntensity = 3f;

    [Header("Keyboard Controls (optional)")]
    public KeyCode rightKey = KeyCode.RightArrow;
    public KeyCode leftKey = KeyCode.LeftArrow;
    public KeyCode startKey = KeyCode.Return;

    private int currentIndex = 0;
    private bool isTransitioning = false;

    private void Start()
    {
        currentIndex = Mathf.Clamp(PlayerPrefs.GetInt("pointer", 0), 0, carSlots.Length - 1);
        PlayerPrefs.SetInt("pointer", currentIndex);

        SnapToCurrentInstantly();
        RefreshAllPanels();
        PersistCarDriveStats();
    }

    private void Update()
    {
        if (Input.GetKeyDown(rightKey)) rightButton();
        else if (Input.GetKeyDown(leftKey)) leftButton();

        if (Input.GetKeyDown(startKey) || Input.GetKeyDown(KeyCode.KeypadEnter)) ConfirmAndStartRace();
    }

    public void rightButton()
    {
        if (!isTransitioning && currentIndex < carSlots.Length - 1) GoToCar(currentIndex + 1);
    }

    public void leftButton()
    {
        if (!isTransitioning && currentIndex > 0) GoToCar(currentIndex - 1);
    }

    // --- Stepper buttons ---

    public void IncreaseAICount() { selectedAICount = Mathf.Clamp(selectedAICount + 1, minAICount, maxAICount); UpdateRaceSetupTexts(); }
    public void DecreaseAICount() { selectedAICount = Mathf.Clamp(selectedAICount - 1, minAICount, maxAICount); UpdateRaceSetupTexts(); }
    public void IncreaseLaps() { selectedLaps = Mathf.Clamp(selectedLaps + 1, minLaps, maxLaps); UpdateRaceSetupTexts(); }
    public void DecreaseLaps() { selectedLaps = Mathf.Clamp(selectedLaps - 1, minLaps, maxLaps); UpdateRaceSetupTexts(); }

    public void NextTrack()
    {
        if (trackOptions == null || trackOptions.Length == 0) return;
        selectedTrackIndex = (selectedTrackIndex + 1) % trackOptions.Length;
        UpdateTrackUI();
    }

    public void PreviousTrack()
    {
        if (trackOptions == null || trackOptions.Length == 0) return;
        selectedTrackIndex = (selectedTrackIndex - 1 + trackOptions.Length) % trackOptions.Length;
        UpdateTrackUI();
    }

    // Wire to the RACE button.
    public void ConfirmAndStartRace()
    {
        if (isTransitioning) return;

        if (!IsCarUnlocked(carSlots[currentIndex]))
        {
            Debug.LogWarning($"ShowroomManager: '{carSlots[currentIndex].displayName}' is locked - " +
                $"requires {PlayerProfile.Tiers[carSlots[currentIndex].requiredBadgeIndex].name} badge. Race not started.");
            return;
        }

        if (trackOptions == null || trackOptions.Length == 0)
        {
            Debug.LogError("ShowroomManager: trackOptions is empty - assign at least one track in the Inspector.");
            return;
        }

        RaceSettings.SelectedVehicleIndex = currentIndex;
        RaceSettings.AICount = selectedAICount;
        RaceSettings.Laps = selectedLaps;
        RaceSettings.SelectedTrackScene = trackOptions[selectedTrackIndex].sceneName;
        RaceSettings.SelectedTrackName = trackOptions[selectedTrackIndex].trackName;
        PersistCarDriveStats();

        LoadingScreenController.LoadScene(RaceSettings.SelectedTrackScene);
    }

    public void GoToMainMenu()
    {
        // Wire to "BACK TO MENU" - adjust the scene name to whatever your actual main menu is.
        LoadingScreenController.LoadScene("MainMenu");
    }

    private void PersistCarDriveStats()
    {
        if (carSlots == null || carSlots.Length == 0) return;

        var all = new RaceSettings.CarDriveStats[carSlots.Length];
        for (int i = 0; i < carSlots.Length; i++)
        {
            all[i] = new RaceSettings.CarDriveStats
            {
                topSpeed = carSlots[i].topSpeed,
                acceleration = carSlots[i].acceleration,
                handling = carSlots[i].handling,
                braking = carSlots[i].braking
            };
        }
        RaceSettings.SetAllCarDriveStats(all);
        PlayerPrefs.Save();
    }

    // --- Internal ---

    private void GoToCar(int index)
    {
        int previousIndex = currentIndex;
        currentIndex = index;
        PlayerPrefs.SetInt("pointer", currentIndex);

        StartCoroutine(TransitionCamera(carSlots[currentIndex].cameraViewpoint, carSlots[currentIndex].carObject.transform));
        StartCoroutine(FadeSpotlight(carSlots[previousIndex].spotlight, 0f));
        StartCoroutine(FadeSpotlight(carSlots[currentIndex].spotlight, activeSpotlightIntensity));

        RefreshAllPanels();
    }

    private void RefreshAllPanels()
    {
        UpdateCarInfoPanel();
        UpdatePageText();
        UpdateRaceSetupTexts();
        UpdateTrackUI();
        UpdateProfileHUD();
    }

    private void UpdateCarInfoPanel()
    {
        var slot = carSlots[currentIndex];
        if (carNameText != null) carNameText.text = slot.displayName;
        if (carClassText != null) carClassText.text = slot.carClass;

        if (carRequiredBadgeIcon != null)
        {
            Sprite sprite = GetBadgeSprite(slot.requiredBadgeIndex);
            carRequiredBadgeIcon.sprite = sprite;
            carRequiredBadgeIcon.enabled = sprite != null;
        }

        SetStatBar(topSpeedFill, topSpeedNumber, slot.topSpeed);
        SetStatBar(accelerationFill, accelerationNumber, slot.acceleration);
        SetStatBar(handlingFill, handlingNumber, slot.handling);
        SetStatBar(brakingFill, brakingNumber, slot.braking);

        if (lockMessageText != null)
        {
            bool locked = !IsCarUnlocked(slot);
            lockMessageText.gameObject.SetActive(locked);
            if (locked)
                lockMessageText.text = $"REQUIRES {PlayerProfile.Tiers[slot.requiredBadgeIndex].name} BADGE";
        }

        // Popup follows the currently viewed car: visible the whole time it's locked,
        // hidden the instant the player switches to an unlocked (or differently locked) car.
        if (lockedPopup != null)
        {
            bool locked = !IsCarUnlocked(slot);
            lockedPopup.SetActive(locked);

            if (locked)
            {
                if (lockedPopupText != null)
                    lockedPopupText.text = "LOCKED";

                if (lockedPopupBadgeIcon != null)
                {
                    Sprite sprite = GetBadgeSprite(slot.requiredBadgeIndex);
                    lockedPopupBadgeIcon.sprite = sprite;
                    lockedPopupBadgeIcon.enabled = sprite != null;
                }
            }
        }
    }

    // True if the player's current badge tier is high enough to race this car.
    private bool IsCarUnlocked(CarSlot slot)
    {
        return PlayerProfile.CurrentBadgeIndex >= slot.requiredBadgeIndex;
    }

    // Shared helper - safely fetches a badge sprite by tier index, used by both
    // the username panel and the locked-car popup so they never get out of sync.
    private Sprite GetBadgeSprite(int tierIndex)
    {
        if (badgeSprites != null && tierIndex >= 0 && tierIndex < badgeSprites.Length)
            return badgeSprites[tierIndex];
        return null;
    }

    private void SetStatBar(Image fillImage, TMP_Text numberText, float value0to10)
    {
        if (fillImage != null) fillImage.fillAmount = Mathf.Clamp01(value0to10 / 10f);
        if (numberText != null) numberText.text = value0to10.ToString("0.0");
    }

    private void UpdatePageText()
    {
        if (pageText != null) pageText.text = $"{currentIndex + 1} / {carSlots.Length}";
    }

    private void UpdateRaceSetupTexts()
    {
        if (aiCountText != null) aiCountText.text = selectedAICount.ToString();
        if (lapsText != null) lapsText.text = selectedLaps.ToString();
    }

    private void UpdateTrackUI()
    {
        if (trackOptions == null || trackOptions.Length == 0)
        {
            if (trackNameText != null) trackNameText.text = "No tracks configured";
            return;
        }

        selectedTrackIndex = Mathf.Clamp(selectedTrackIndex, 0, trackOptions.Length - 1);
        TrackOption track = trackOptions[selectedTrackIndex];

        if (trackNameText != null) trackNameText.text = track.trackName;
        if (trackSubtitleText != null) trackSubtitleText.text = track.trackSubtitle;
        if (trackPreviewImage != null)
        {
            trackPreviewImage.sprite = track.previewImage;
            trackPreviewImage.enabled = track.previewImage != null;
        }
    }

    // Badge + XP progress here is driven by LIFETIME XP (PlayerProfile), not the
    // level-up XP bar in PlayerEconomy - badges only ever climb, they never reset.
    private void UpdateProfileHUD()
    {
        if (usernameText != null) usernameText.text = PlayerProfile.Username.ToUpper();
        if (badgeNameText != null) badgeNameText.text = PlayerProfile.CurrentBadgeName;

        if (badgeIcon != null)
        {
            Sprite sprite = GetBadgeSprite(PlayerProfile.CurrentBadgeIndex);
            badgeIcon.sprite = sprite;
            badgeIcon.enabled = sprite != null;
        }

        int index = PlayerProfile.CurrentBadgeIndex;
        bool maxTier = index >= PlayerProfile.Tiers.Length - 1;

        if (xpBarFill != null)
            xpBarFill.fillAmount = PlayerProfile.ProgressToNextBadge;

        if (xpProgressText != null)
        {
            if (maxTier)
            {
                xpProgressText.text = "MAX BADGE";
            }
            else
            {
                int currentFloor = PlayerProfile.Tiers[index].xpRequired;
                int nextCeiling = PlayerProfile.Tiers[index + 1].xpRequired;
                int xp = PlayerProfile.LifetimeXP;
                xpProgressText.text = $"{(xp - currentFloor):N0} / {(nextCeiling - currentFloor):N0}";
            }
        }
    }

    private IEnumerator TransitionCamera(Transform viewpoint, Transform lookTarget)
    {
        isTransitioning = true;

        Vector3 startPos = Camera.main.transform.position;
        Quaternion startRot = Camera.main.transform.rotation;

        Vector3 endPos = viewpoint.position;
        Quaternion endRot = Quaternion.LookRotation(lookTarget.position - endPos, Vector3.up);

        float t = 0f;
        while (t < cameraTransitionDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / cameraTransitionDuration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);

            Vector3 currentPos = Vector3.Lerp(startPos, endPos, eased);
            Camera.main.transform.position = currentPos;

            Quaternion currentLookRot = Quaternion.LookRotation(lookTarget.position - currentPos, Vector3.up);
            Camera.main.transform.rotation = Quaternion.Slerp(startRot, currentLookRot, eased);

            yield return null;
        }

        Camera.main.transform.position = endPos;
        Camera.main.transform.rotation = Quaternion.LookRotation(lookTarget.position - endPos, Vector3.up);
        isTransitioning = false;
    }

    private IEnumerator FadeSpotlight(Light light, float targetIntensity)
    {
        if (light == null) yield break;

        while (Mathf.Abs(light.intensity - targetIntensity) > 0.01f)
        {
            light.intensity = Mathf.MoveTowards(light.intensity, targetIntensity, spotlightFadeSpeed * Time.deltaTime);
            yield return null;
        }
        light.intensity = targetIntensity;
    }

    private void SnapToCurrentInstantly()
    {
        for (int i = 0; i < carSlots.Length; i++)
        {
            if (carSlots[i].spotlight != null)
                carSlots[i].spotlight.intensity = (i == currentIndex) ? activeSpotlightIntensity : 0f;
        }

        Transform startView = carSlots[currentIndex].cameraViewpoint;
        Transform startCar = carSlots[currentIndex].carObject.transform;
        Camera.main.transform.position = startView.position;
        Camera.main.transform.rotation = Quaternion.LookRotation(startCar.position - startView.position, Vector3.up);
    }
}