using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Put this on an empty "SceneController" GameObject in your intro/loading scene
// (the scene that plays right after Splash, before Showroom).
//
// What it does:
// 1. Spawns a car at spawnPoint.
//    - If the player has never picked a car yet, it spawns a RANDOM prefab from carPrefabs.
//    - If the player already picked a car in the Showroom before, it spawns THAT car
//      (reads the same RaceSettings.SelectedVehicleIndex your ShowroomManager already writes).
// 2. Loops the camera through a list of angle transforms you set up in the scene
//    (far, close, top-down, bottom-up, left-right, etc.), fading to black between each cut.
// 3. Loops forever until the player taps "Go To Garage", which stops the loop and loads
//    the Showroom scene.
public class CarShowcaseIntroController : MonoBehaviour
{
    [Header("Car Setup")]
    [Tooltip("Same array/order as your ShowroomManager carSlots / vehicleList.vehicles.")]
    public GameObject[] carPrefabs;
    public Transform spawnPoint;

    [Header("Camera Angles")]
    [Tooltip("Empty GameObjects placed around the car: far, close, top-down, bottom-up, left, right, etc. Camera will cycle through these in order, looping back to 0.")]
    public Transform[] cameraAngles;
    public Camera introCamera; // leave empty to use Camera.main

    [Header("Fade (assign a full-screen black Image's CanvasGroup)")]
    public CanvasGroup fadeCanvasGroup;
    public float fadeOutDuration = 0.5f; // to black
    public float fadeInDuration = 0.6f;  // from black
    public float holdPerAngle = 2.5f;    // how long to sit on each angle before cutting

    [Header("Navigation")]
    public Button goToGarageButton;
    public string showroomSceneName = "Showroom";

    [Header("First-Time Name Entry")]
    [Tooltip("Panel with the name InputField + Confirm button. Shown only once, the " +
             "first time the player taps 'Go To Garage' (PlayerProfile.HasSetName == false). " +
             "Every launch after that, tapping Go To Garage skips straight to the Showroom.")]
    public GameObject nameEntryPanel;
    public TMPro.TMP_InputField nameInputField;
    public Button confirmNameButton;

    [Header("Background Music")]
    public AudioSource musicSource;
    public float musicTargetVolume = 0.6f;
    public float musicFadeInDuration = 1.5f;
    public float musicFadeOutDuration = 0.5f; // when leaving to Garage

    private GameObject spawnedCar;
    private Coroutine loopRoutine;
    private bool isLeaving = false;

    private void Start()
    {
        SpawnCar();

        if (goToGarageButton != null)
            goToGarageButton.onClick.AddListener(GoToGarage);

        if (nameEntryPanel != null)
        {
            nameEntryPanel.SetActive(false);
            if (confirmNameButton != null)
                confirmNameButton.onClick.AddListener(OnConfirmName);
        }

        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = 0f;

        if (introCamera == null) introCamera = Camera.main;

        loopRoutine = StartCoroutine(CameraShowcaseLoop());

        if (musicSource != null)
        {
            musicSource.volume = 0f;
            musicSource.Play();
            StartCoroutine(FadeMusic(0f, musicTargetVolume, musicFadeInDuration));
        }
    }

    private IEnumerator FadeMusic(float from, float to, float duration)
    {
        if (musicSource == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        musicSource.volume = to;
    }

    private void SpawnCar()
    {
        if (carPrefabs == null || carPrefabs.Length == 0 || spawnPoint == null)
        {
            Debug.LogWarning("CarShowcaseIntroController: carPrefabs or spawnPoint not assigned.");
            return;
        }

        // Always show a random car in this scene, regardless of what was
        // previously picked in the Showroom (that selection is only used
        // once the player actually enters the race).
        int indexToShow = Random.Range(0, carPrefabs.Length);

        spawnedCar = Instantiate(carPrefabs[indexToShow], spawnPoint.position, spawnPoint.rotation);
    }

    private IEnumerator CameraShowcaseLoop()
    {
        if (cameraAngles == null || cameraAngles.Length == 0 || introCamera == null)
            yield break;

        int angleIndex = 0;

        // Snap to the first angle instantly, faded to black, then fade in.
        SnapCameraTo(cameraAngles[0]);
        yield return StartCoroutine(Fade(0f, 1f, 0.01f)); // ensure black
        yield return StartCoroutine(Fade(1f, 0f, fadeInDuration));

        while (!isLeaving)
        {
            yield return new WaitForSeconds(holdPerAngle);
            if (isLeaving) yield break;

            // Fade to black
            yield return StartCoroutine(Fade(0f, 1f, fadeOutDuration));

            // Cut to next angle while screen is black
            angleIndex = (angleIndex + 1) % cameraAngles.Length;
            SnapCameraTo(cameraAngles[angleIndex]);

            if (isLeaving) yield break;

            // Fade back in
            yield return StartCoroutine(Fade(1f, 0f, fadeInDuration));
        }
    }

    private void SnapCameraTo(Transform angle)
    {
        introCamera.transform.position = angle.position;
        introCamera.transform.rotation = angle.rotation;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        float t = 0f;
        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = to;
            yield break;
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        fadeCanvasGroup.alpha = to;
    }

    public void GoToGarage()
    {
        if (isLeaving) return;

        // First time ever tapping this button: pause the loop, ask for a name,
        // and only actually leave once it's confirmed. Every launch after that,
        // PlayerProfile.HasSetName is already true and this block is skipped.
        if (nameEntryPanel != null && !PlayerProfile.HasSetName)
        {
            if (loopRoutine != null) StopCoroutine(loopRoutine);
            nameEntryPanel.SetActive(true);
            if (nameInputField != null)
                nameInputField.Select();
            return;
        }

        isLeaving = true;

        if (loopRoutine != null) StopCoroutine(loopRoutine);
        StartCoroutine(FadeOutAndLoad());
    }

    [Tooltip("Optional - shown briefly if the player taps Confirm with an empty name field.")]
    public TMPro.TMP_Text nameEntryErrorText;

    // Wired to confirmNameButton.onClick. Also safe to hook to the InputField's
    // "On Submit" event (Enter key) if you want that too.
    private void OnConfirmName()
    {
        string typedName = nameInputField != null ? nameInputField.text : "";

        // Require an actual name instead of silently falling back to "Racer" -
        // makes it obvious in testing whether typing is actually registering.
        if (string.IsNullOrWhiteSpace(typedName))
        {
            if (nameEntryErrorText != null)
                nameEntryErrorText.text = "Please type a name first.";
            else
                Debug.LogWarning("CarShowcaseIntroController: name field is empty, not confirming yet.");
            return;
        }

        PlayerProfile.SetUsername(typedName);

        nameEntryPanel.SetActive(false);
        isLeaving = true;
        StartCoroutine(FadeOutAndLoad());
    }

    private IEnumerator FadeOutAndLoad()
    {
        if (musicSource != null)
            StartCoroutine(FadeMusic(musicSource.volume, 0f, musicFadeOutDuration));

        yield return StartCoroutine(Fade(fadeCanvasGroup != null ? fadeCanvasGroup.alpha : 0f, 1f, fadeOutDuration));
        LoadingScreenController.LoadScene(showroomSceneName);
    }
}