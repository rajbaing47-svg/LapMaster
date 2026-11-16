using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Drop this in a dedicated "Loading" scene (see setup steps).
// Any other script that wants to change scenes should call
// LoadingScreenController.LoadScene("SceneName") instead of
// SceneManager.LoadScene("SceneName") directly. That's the only change
// needed elsewhere - this script handles the rest.
public class LoadingScreenController : MonoBehaviour
{
    [Header("Background Images")]
    [Tooltip("Drag in 4-6 fullscreen splash/tip images. One is picked at random each time the loading scene shows.")]
    public Sprite[] backgroundImages;
    public Image backgroundImage;

    [Header("Progress UI")]
    public Slider progressBar;              // optional, assign if you have a Slider
    public TextMeshProUGUI progressText;     // optional, shows "42%"
    public TextMeshProUGUI tipText;          // optional, e.g. "Tip: drift to build boost"

    [Header("Loading Tips (optional)")]
    [Tooltip("Random tip shown alongside the image. Leave empty to skip.")]
    public string[] loadingTips;

    [Header("Timing")]
    [Tooltip("Loading screen stays up at least this long, even if the target scene is ready sooner - prevents a 1-frame flash. Kept at 3-4s on the race-finish -> Score transition so it reads as the game actually calculating final results, not a placeholder flash.")]
    public float minimumDisplayTime = 3.5f;

    // Static field survives the scene switch into the Loading scene - GameManager scenes
    // can't pass parameters directly through SceneManager.LoadScene, so we stash the target
    // scene name here right before loading, and this script picks it back up in Start().
    private static string targetSceneName;

    // Call this from ANY script instead of SceneManager.LoadScene(sceneName).
    // e.g. replace:  SceneManager.LoadScene(raceSceneName);
    // with:          LoadingScreenController.LoadScene(raceSceneName);
    public static void LoadScene(string sceneName)
    {
        targetSceneName = sceneName;
        SceneManager.LoadScene("Loading");
    }

    void Start()
    {
        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning("LoadingScreenController: no target scene was set - " +
                "did something call SceneManager.LoadScene(\"Loading\") directly instead of LoadingScreenController.LoadScene(sceneName)?");
            return;
        }

        ShowRandomBackground();
        ShowRandomTip();
        StartCoroutine(LoadTargetScene());
    }

    void ShowRandomBackground()
    {
        if (backgroundImage == null || backgroundImages == null || backgroundImages.Length == 0) return;
        backgroundImage.sprite = backgroundImages[Random.Range(0, backgroundImages.Length)];
    }

    void ShowRandomTip()
    {
        if (tipText == null || loadingTips == null || loadingTips.Length == 0) return;
        tipText.text = loadingTips[Random.Range(0, loadingTips.Length)];
    }

    IEnumerator LoadTargetScene()
    {
        float startTime = Time.time;

        AsyncOperation op = SceneManager.LoadSceneAsync(targetSceneName);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            // op.progress goes 0 -> 0.9 while loading, then sits at 0.9 until
            // allowSceneActivation is allowed to flip true. Remap to a clean 0-1 range.
            float rawProgress = Mathf.Clamp01(op.progress / 0.9f);

            // Also factor in our minimum-display-time so the bar doesn't hit 100%
            // instantly on a fast load and then just sit there awkwardly.
            float timeProgress = Mathf.Clamp01((Time.time - startTime) / minimumDisplayTime);
            float displayProgress = Mathf.Min(rawProgress, timeProgress);

            SetProgressUI(displayProgress);

            bool sceneReady = op.progress >= 0.9f;
            bool minTimeElapsed = (Time.time - startTime) >= minimumDisplayTime;

            if (sceneReady && minTimeElapsed)
            {
                SetProgressUI(1f);
                op.allowSceneActivation = true;
            }

            yield return null;
        }
    }

    void SetProgressUI(float value01)
    {
        if (progressBar != null) progressBar.value = value01;
        if (progressText != null) progressText.text = Mathf.RoundToInt(value01 * 100f) + "%";
    }
}