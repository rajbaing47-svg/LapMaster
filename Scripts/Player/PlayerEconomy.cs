using UnityEngine;

// Same pattern as RaceSettings.cs - plain static class backed by PlayerPrefs so
// currency/level/XP survive scene loads AND persist between sessions.
// Drives the TopBar_HUD (coin count, level badge, "ROOKIE" rank text, XP bar).
public static class PlayerEconomy
{
    private const string KeyCurrency = "Economy_Currency";
    private const string KeyLevel = "Economy_Level";
    private const string KeyXP = "Economy_XP";
    private const string KeyXPToNext = "Economy_XPToNext";
    private const string KeyRankName = "Economy_RankName";

    public static int Currency
    {
        get => PlayerPrefs.GetInt(KeyCurrency, 0);
        set => PlayerPrefs.SetInt(KeyCurrency, Mathf.Max(0, value));
    }

    public static int Level
    {
        get => PlayerPrefs.GetInt(KeyLevel, 1);
        set => PlayerPrefs.SetInt(KeyLevel, Mathf.Max(1, value));
    }

    public static int XP
    {
        get => PlayerPrefs.GetInt(KeyXP, 0);
        set => PlayerPrefs.SetInt(KeyXP, Mathf.Max(0, value));
    }

    // XP required to reach the next level - shown as the "1,850 / 3,000" text.
    public static int XPToNextLevel
    {
        get => PlayerPrefs.GetInt(KeyXPToNext, 3000);
        set => PlayerPrefs.SetInt(KeyXPToNext, Mathf.Max(1, value));
    }

    public static string RankName
    {
        get => PlayerPrefs.GetString(KeyRankName, "ROOKIE");
        set => PlayerPrefs.SetString(KeyRankName, value);
    }

    // Call this after a race finishes to reward the player and auto-level-up
    // once XP crosses the threshold. Hook it into ScoreManager/CheckpointManager
    // wherever you compute race rewards.
    public static void AddReward(int coins, int xpGained)
    {
        Currency += coins;
        XP += xpGained;

        // Lifetime XP powers PlayerProfile's badge tiers (Rookie/Bronze/Silver/...).
        // It never resets, unlike XP above which loops back to 0 on every level-up.
        PlayerProfile.AddLifetimeXP(xpGained);

        while (XP >= XPToNextLevel)
        {
            XP -= XPToNextLevel;
            Level += 1;
            XPToNextLevel = Mathf.RoundToInt(XPToNextLevel * 1.15f); // next level needs a bit more
        }

        PlayerPrefs.Save();
    }
}