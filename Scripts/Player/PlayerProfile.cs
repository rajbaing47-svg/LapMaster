using UnityEngine;

// Same pattern as PlayerEconomy.cs / RaceSettings.cs - plain static class backed by
// PlayerPrefs so it survives scene loads AND persists between sessions.
//
// Handles two things:
//   1. The player's chosen name (set once, on first launch, via the loading screen panel).
//   2. Badge tier, derived from LIFETIME XP earned (never decreases, unlike PlayerEconomy.XP
//      which resets to 0 every time you level up). This means badges only ever go up,
//      even though the level-up XP bar keeps looping.
//
// Hook LifetimeXP.Add(xp) in the same place you call PlayerEconomy.AddReward(coins, xp),
// so both systems move together.
public static class PlayerProfile
{
    private const string KeyHasSetName = "Profile_HasSetName";
    private const string KeyUsername = "Profile_Username";
    private const string KeyLifetimeXP = "Profile_LifetimeXP";

    // ---------------- Name / first-launch ----------------

    public static bool HasSetName
    {
        get => PlayerPrefs.GetInt(KeyHasSetName, 0) == 1;
    }

    public static string Username
    {
        get => PlayerPrefs.GetString(KeyUsername, "Racer");
        private set => PlayerPrefs.SetString(KeyUsername, value);
    }

    // Call this from the name-entry panel when the player confirms their name.
    // Trims whitespace, enforces a max length, falls back to "Racer" if left blank.
    public static void SetUsername(string name)
    {
        string clean = string.IsNullOrWhiteSpace(name) ? "Racer" : name.Trim();
        if (clean.Length > 16) clean = clean.Substring(0, 16);

        Username = clean;
        PlayerPrefs.SetInt(KeyHasSetName, 1);
        PlayerPrefs.Save();
    }

    // ---------------- Lifetime XP / Badges ----------------

    public static int LifetimeXP
    {
        get => PlayerPrefs.GetInt(KeyLifetimeXP, 0);
        private set => PlayerPrefs.SetInt(KeyLifetimeXP, Mathf.Max(0, value));
    }

    // Call alongside PlayerEconomy.AddReward so lifetime XP always climbs,
    // independent of the level-up XP bar resetting.
    public static void AddLifetimeXP(int xp)
    {
        LifetimeXP += Mathf.Max(0, xp);
        PlayerPrefs.Save();
    }

    [System.Serializable]
    public struct BadgeTier
    {
        public string name;
        public int xpRequired;
        public BadgeTier(string name, int xpRequired)
        {
            this.name = name;
            this.xpRequired = xpRequired;
        }
    }

    // Edit thresholds/names here to retune progression - everything else reads from this list.
    // Index also doubles as the sprite index in ShowroomProfileUI.badgeSprites.
    public static readonly BadgeTier[] Tiers = new BadgeTier[]
    {
        new BadgeTier("ROOKIE",  0),
        new BadgeTier("AMATEUR", 1500),
        new BadgeTier("PRO",     5000),
        new BadgeTier("ELITE",   15000),
        new BadgeTier("LEGEND",  35000),
    };

    // Current badge tier index, based on lifetime XP.
    public static int CurrentBadgeIndex
    {
        get
        {
            int xp = LifetimeXP;
            int index = 0;
            for (int i = 0; i < Tiers.Length; i++)
            {
                if (xp >= Tiers[i].xpRequired) index = i;
                else break;
            }
            return index;
        }
    }

    public static string CurrentBadgeName => Tiers[CurrentBadgeIndex].name;

    // 0..1 progress toward the NEXT badge tier. Returns 1 if already at max tier.
    public static float ProgressToNextBadge
    {
        get
        {
            int index = CurrentBadgeIndex;
            if (index >= Tiers.Length - 1) return 1f;

            int currentFloor = Tiers[index].xpRequired;
            int nextCeiling = Tiers[index + 1].xpRequired;
            int xp = LifetimeXP;

            return Mathf.Clamp01((float)(xp - currentFloor) / (nextCeiling - currentFloor));
        }
    }
}