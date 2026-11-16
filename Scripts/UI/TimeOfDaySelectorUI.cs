using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Put this on a UI object in the Showroom (Garage) scene, next to your track-select panel.
// Shows the currently selected TimeOfDay as text (e.g. "Time: Day") and gives you 3 button
// methods to hook up (Day / Evening / Night). Changing it here writes to RaceSettings, so
// whichever track scene loads next reads it via TrackLightingController.Start() and applies
// the matching skybox + lights automatically - this script never touches lights/skybox
// itself, it only shows and changes the choice.
public class TimeOfDaySelectorUI : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Assign a TextMeshProUGUI to show the current selection, e.g. 'Time: Evening'")]
    public TMP_Text timeLabel;

    [Header("Optional: highlight the selected button (set to non-interactable or dim the others)")]
    public Button dayButton;
    public Button eveningButton;
    public Button nightButton;

    [Header("Optional: colors for the selected vs unselected button")]
    public Color selectedColor = Color.white;
    public Color unselectedColor = new Color(1f, 1f, 1f, 0.5f);

    private void OnEnable()
    {
        // Refresh every time this panel becomes active, in case the value changed
        // elsewhere (or the player re-opens the garage after a race).
        RefreshDisplay();
    }

    // Hook these to the 3 buttons' OnClick() in the Inspector.
    public void SetTimeDay()
    {
        RaceSettings.SelectedTimeOfDay = TimeOfDay.Day;
        RefreshDisplay();
    }

    public void SetTimeEvening()
    {
        RaceSettings.SelectedTimeOfDay = TimeOfDay.Evening;
        RefreshDisplay();
    }

    public void SetTimeNight()
    {
        RaceSettings.SelectedTimeOfDay = TimeOfDay.Night;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        TimeOfDay current = RaceSettings.SelectedTimeOfDay;

        if (timeLabel != null)
            timeLabel.text = "Time: " + current;

        UpdateButtonHighlight(dayButton, current == TimeOfDay.Day);
        UpdateButtonHighlight(eveningButton, current == TimeOfDay.Evening);
        UpdateButtonHighlight(nightButton, current == TimeOfDay.Night);
    }

    private void UpdateButtonHighlight(Button button, bool isSelected)
    {
        if (button == null) return;
        Image img = button.GetComponent<Image>();
        if (img != null)
            img.color = isSelected ? selectedColor : unselectedColor;
    }
}