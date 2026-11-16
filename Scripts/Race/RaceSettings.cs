using System.Globalization;
using UnityEngine;

// Plain static class (no MonoBehaviour, nothing to attach to a GameObject).
// Static fields survive a SceneManager.LoadScene() call on their own, but they used to
// reset the moment you stopped Play Mode (or quit a build) - so picking a car/track/lap
// count, closing the game, and reopening it always fell back to the hardcoded defaults.
//
// UPDATE: every field is now backed by PlayerPrefs, so choices persist across sessions
// (Play Mode stop/start, or quitting and relaunching a build) while keeping the exact
// same public API - ShowroomManager/RaceManager/ScoreManager all still just read/write
// RaceSettings.SelectedVehicleIndex etc. exactly as before, nothing else needed to change.
//
// UPDATE 2: added TimeOfDay (Day/Evening/Night). Player picks it in the Showroom (Garage)
// scene, it's saved to PlayerPrefs the same way as SelectedTrackScene, and the track scene
// reads RaceSettings.SelectedTimeOfDay on load (see TrackLightingController) to pick the
// right skybox, sun settings, and turn street lamps / car headlights on or off.
public enum TimeOfDay
{
    Day = 0,
    Evening = 1,
    Night = 2
}

public static class RaceSettings
{
    private const string KeySelectedVehicleIndex = "RaceSettings_SelectedVehicleIndex";
    private const string KeyAICount = "RaceSettings_AICount";
    private const string KeyLaps = "RaceSettings_Laps";
    private const string KeySelectedTrackScene = "RaceSettings_SelectedTrackScene";
    private const string KeySelectedTrackName = "RaceSettings_SelectedTrackName";
    private const string KeyTimeOfDay = "RaceSettings_TimeOfDay";
    private const string KeyCarDriveStats = "RaceSettings_CarDriveStats";

    // Per-car 0-10 Showroom bars, same order as ShowroomManager.carSlots / vehicleList.vehicles.
    public struct CarDriveStats
    {
        public float topSpeed;
        public float acceleration;
        public float handling;
        public float braking;
    }

    // Index into vehicleList.vehicles - which car the player picked in the showroom.
    public static int SelectedVehicleIndex
    {
        get => PlayerPrefs.GetInt(KeySelectedVehicleIndex, 0);
        set => PlayerPrefs.SetInt(KeySelectedVehicleIndex, value);
    }

    // How many AI opponents to spawn. Actual clamp range comes from ShowroomManager's
    // minAICount/maxAICount (popup) and RaceManager's maxAICars (spawn safety net) - keep all three in sync.
    public static int AICount
    {
        get => PlayerPrefs.GetInt(KeyAICount, 3);
        set => PlayerPrefs.SetInt(KeyAICount, value);
    }

    // How many laps the race should run for.
    public static int Laps
    {
        get => PlayerPrefs.GetInt(KeyLaps, 3);
        set => PlayerPrefs.SetInt(KeyLaps, value);
    }

    // Exact scene name of whichever track the player picked in the setup popup,
    // e.g. "Track1", "Track2", "Track3". ShowroomManager writes this right before loading
    // the race scene. ScoreManager reads it back for "Restart Race" so replaying always
    // reloads the SAME track that was just raced, not a hardcoded one.
    // Default is "First" so pressing Play in the garage/play scene (First.unity) matches
    // SceneManager.GetActiveScene().name and cars are not frozen by the controller check.
    public static string SelectedTrackScene
    {
        get => PlayerPrefs.GetString(KeySelectedTrackScene, "First");
        set => PlayerPrefs.SetString(KeySelectedTrackScene, value);
    }

    // Friendly display name of whichever track the player picked in the Showroom,
    // e.g. "CITY CIRCUIT" (from ShowroomManager.TrackOption.trackName). ShowroomManager
    // writes this right before loading the race scene, alongside SelectedTrackScene.
    // ScoreManager reads it back to show the correct track name on the results screen,
    // instead of a hardcoded Inspector value. Default falls back to the scene name.
    public static string SelectedTrackName
    {
        get => PlayerPrefs.GetString(KeySelectedTrackName, SelectedTrackScene);
        set => PlayerPrefs.SetString(KeySelectedTrackName, value);
    }

    // Which time-of-day the player picked in the Showroom (Day/Evening/Night).
    // TrackLightingController reads this on Start() in the track scene to pick the
    // matching skybox, sun color/intensity, and switch street lamps + car lights on/off.
    // Default is Day, matching the game's current/original look with nothing else changed.
    public static TimeOfDay SelectedTimeOfDay
    {
        get => (TimeOfDay)PlayerPrefs.GetInt(KeyTimeOfDay, (int)TimeOfDay.Day);
        set => PlayerPrefs.SetInt(KeyTimeOfDay, (int)value);
    }

    public static void SetAllCarDriveStats(CarDriveStats[] all)
    {
        if (all == null || all.Length == 0)
        {
            PlayerPrefs.DeleteKey(KeyCarDriveStats);
            return;
        }

        var parts = new string[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            parts[i] = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                all[i].topSpeed, all[i].acceleration, all[i].handling, all[i].braking);
        }
        PlayerPrefs.SetString(KeyCarDriveStats, string.Join("|", parts));
    }

    public static bool TryGetCarDriveStats(int index, out CarDriveStats stats)
    {
        stats = default;
        if (index < 0) return false;

        string raw = PlayerPrefs.GetString(KeyCarDriveStats, string.Empty);
        if (string.IsNullOrEmpty(raw)) return false;

        string[] cars = raw.Split('|');
        if (index >= cars.Length) return false;

        string[] values = cars[index].Split(',');
        if (values.Length != 4) return false;

        return float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out stats.topSpeed)
            && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out stats.acceleration)
            && float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out stats.handling)
            && float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out stats.braking);
    }

    // NEW: call this if you ever want to wipe saved choices back to defaults
    // (e.g. a "Reset Settings" button in a menu).
    public static void ResetToDefaults()
    {
        PlayerPrefs.DeleteKey(KeySelectedVehicleIndex);
        PlayerPrefs.DeleteKey(KeyAICount);
        PlayerPrefs.DeleteKey(KeyLaps);
        PlayerPrefs.DeleteKey(KeySelectedTrackScene);
        PlayerPrefs.DeleteKey(KeySelectedTrackName);
        PlayerPrefs.DeleteKey(KeyTimeOfDay);
        PlayerPrefs.DeleteKey(KeyCarDriveStats);
        PlayerPrefs.Save();
    }
}