using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach this to the button GameObject (or any object) in EACH of the 4 places
// you need this behavior: Garage settings icon, Score settings icon, Loading
// settings icon, and the Race scene's pause button.
//
// Same script does both jobs - the only difference between "settings icon" and
// "pause button" is the pauseGameplay flag:
//   - Garage / Score / Loading  -> leave pauseGameplay UNTICKED
//   - Race scene pause button   -> TICK pauseGameplay (freezes Time.timeScale
//                                  so the car/physics/AI actually stop while
//                                  the panel is open)
//
// SETUP IN THE EDITOR (same steps in all 4 scenes):
//   1. Create your gear-icon / pause-icon Button in the Canvas.
//   2. Create the Panel GameObject (the one holding Badge Path / How To Play /
//      Quit Game buttons, or Restart/Garage/Quit for the race scene). Leave it
//      set INACTIVE by default in the Hierarchy (untick the checkbox next to
//      its name in the Inspector) so it starts hidden.
//   3. Add this PanelToggle component to the Button (or an empty manager object).
//   4. Drag the Panel GameObject into the "panel" field below.
//   5. On the Button component itself, scroll to "On Click ()", click "+",
//      drag this same GameObject in, and pick PanelToggle -> TogglePanel().
//   6. (Race scene only) tick pauseGameplay.
//
// That's it - clicking the button OR pressing Esc opens/closes the panel
// (works the same in every scene).
public class PanelToggle : MonoBehaviour
{
    [Tooltip("The panel this button opens and closes. Assign the Panel GameObject here - leave it inactive in the Hierarchy by default.")]
    public GameObject panel;

    [Tooltip("Tick this ONLY on the race scene's pause button. Freezes Time.timeScale to 0 while the panel is open, so the car/AI/physics actually stop instead of continuing to move behind the panel.")]
    public bool pauseGameplay = false;

    [Tooltip("Optional: an AudioSource to play a click sound when the panel opens/closes. Leave empty to skip.")]
    public AudioSource clickSound;

    [Tooltip("TEMPORARY DEBUG: when ticked, every mouse click/tap logs which UI object received it (or if no EventSystem exists). Use this to find out what is blocking the pause button when you arrive from the Showroom. Untick when done.")]
    public bool debugClicks = false;

    private void Awake()
    {
        EnsureEventSystem();

        // Safety: make sure we start closed and time isn't left frozen from a
        // previous play session / domain reload, no matter what state the
        // Panel's checkbox was accidentally left in.
        if (panel != null) panel.SetActive(false);
        if (pauseGameplay) Time.timeScale = 1f;
    }

    // Without an EventSystem in the scene, NO UI button can be clicked (but Esc
    // still works because it doesn't use the UI). If the scene somehow has none
    // when it loads, create one so the pause button always responds.
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindObjectOfType<EventSystem>() != null) return;
        Debug.LogWarning("PanelToggle: no EventSystem found in this scene - creating one so UI buttons work.");
        var go = new GameObject("EventSystem (auto)");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void LogClickTarget()
    {
        if (EventSystem.current == null)
        {
            Debug.LogWarning("PanelToggle DEBUG: click happened but EventSystem.current is NULL - UI can't receive clicks.");
            return;
        }
        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        if (hits.Count == 0) { Debug.Log("PanelToggle DEBUG: click hit NO UI element."); return; }
        string top = hits[0].gameObject.name;
        Debug.Log($"PanelToggle DEBUG: click hit {hits.Count} UI object(s). TOP (the one that gets the click) = '{top}'. " +
                  "Full order: " + string.Join(" > ", hits.ConvertAll(h => h.gameObject.name)));
    }

    private void Update()
    {
        if (debugClicks && Input.GetMouseButtonDown(0)) LogClickTarget();

        // Esc now works exactly like clicking the on-screen button: it opens
        // the panel if it's closed, and closes it if it's open.
        if (panel != null && Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePanel();
        }
    }

    // Hook this to the settings/pause Button's OnClick().
    public void TogglePanel()
    {
        if (panel == null)
        {
            Debug.LogError($"{name}: PanelToggle has no panel assigned in the Inspector.");
            return;
        }

        if (panel.activeSelf) ClosePanel();
        else OpenPanel();
    }

    public void OpenPanel()
    {
        panel.SetActive(true);
        if (pauseGameplay) Time.timeScale = 0f;
        PlayClick();
    }

    // Also handy to hook to an explicit "X" / close button INSIDE the panel
    // itself, in addition to the toggle button and Esc.
    public void ClosePanel()
    {
        panel.SetActive(false);
        if (pauseGameplay) Time.timeScale = 1f;
        PlayClick();
    }

    private void PlayClick()
    {
        if (clickSound != null) clickSound.Play();
    }

    // FIX: if this object gets disabled/destroyed (e.g. scene unload) while
    // the panel had frozen time, un-freeze it - otherwise Time.timeScale can
    // stay stuck at 0 into the next scene.
    private void OnDisable()
    {
        if (pauseGameplay) Time.timeScale = 1f;
    }
}