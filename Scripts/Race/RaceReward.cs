// Same pattern as RaceResults.cs / RaceSettings.cs - plain static class, survives the
// scene load from the race track to Score without needing a DontDestroyOnLoad object.
// CheckpointManager.FinishRace() writes this the instant the player crosses the line;
// ScoreManager reads it to show "+XP gained this race" on the results screen.
public static class RaceReward
{
    public static int XPGained { get; set; }
    public static int CoinsGained { get; set; }
    public static string BadgeNameAtFinish { get; set; } // in case the player ranked up mid-race
}