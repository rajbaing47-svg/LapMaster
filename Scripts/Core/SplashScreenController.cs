using UnityEngine;
using UnityEngine.SceneManagement;

public class SplashScreenController : MonoBehaviour
{
    [Header("Assign the CanvasGroup on your text/panel")]
    public CanvasGroup canvasGroup;

    [Header("Timing")]
    public float fadeInDuration = 1f;
    public float holdDuration = 1.5f;
    public float fadeOutDuration = 1f;

    [Header("Next Scene")]
    public string nextSceneName = "First"; // your main menu/showroom scene

    [Header("First-Time Name Entry")]
    [Tooltip("Assign the panel containing the name InputField + Confirm button. " +
             "Only shown once, on the very first launch (PlayerProfile.HasSetName == false). " +
             "Leave empty if you don't want this feature wired up yet.")]
    public GameObject nameEntryPanel;
    [Tooltip("TMP_InputField on the name entry panel.")]
    public TMPro.TMP_InputField nameInputField;
    [Tooltip("Button the player taps to confirm their name and continue.")]
    public UnityEngine.UI.Button confirmNameButton;

    private bool waitingForName = false;

    private void Start()
    {
        canvasGroup.alpha = 0f;

        if (nameEntryPanel != null)
        {
            nameEntryPanel.SetActive(false);
            if (confirmNameButton != null)
                confirmNameButton.onClick.AddListener(OnConfirmName);
        }

        StartCoroutine(PlaySplash());
    }

    private System.Collections.IEnumerator PlaySplash()
    {
        // Fade in
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        canvasGroup.alpha = 1f;

        // Hold
        yield return new WaitForSeconds(holdDuration);

        // First launch only: pause here and let the player type their name
        // before we ever fade out / load the next scene.
        if (nameEntryPanel != null && !PlayerProfile.HasSetName)
        {
            waitingForName = true;
            nameEntryPanel.SetActive(true);
            if (nameInputField != null)
                nameInputField.Select();

            while (waitingForName)
                yield return null;

            nameEntryPanel.SetActive(false);
        }

        // Fade out
        t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(t / fadeOutDuration);
            yield return null;
        }
        canvasGroup.alpha = 0f;

        LoadingScreenController.LoadScene(nextSceneName);
    }

    // Wired to confirmNameButton.onClick. Also safe to call from the InputField's
    // "On Submit" event (Enter key) if you want that too.
    private void OnConfirmName()
    {
        string typedName = nameInputField != null ? nameInputField.text : "";
        PlayerProfile.SetUsername(typedName);
        waitingForName = false;
    }
}