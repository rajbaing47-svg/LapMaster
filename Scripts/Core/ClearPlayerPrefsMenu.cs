#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// TEMPORARY TESTING TOOL ONLY - delete this file before you build/ship the game.
// Adds a menu item so you can wipe PlayerPrefs (username, XP, everything) without
// digging through the Windows registry, so you can re-test the first-launch flow.
public static class ClearPlayerPrefsMenu
{
    [MenuItem("Tools/Clear All PlayerPrefs (Testing)")]
    public static void ClearAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("PlayerPrefs cleared. PlayerProfile.HasSetName is now false again - the name panel will show on next 'Go To Garage' tap.");
    }
}
#endif