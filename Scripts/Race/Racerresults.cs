using System.Collections.Generic;

// Plain static class, same pattern as RaceSettings.cs - survives the scene load from
// F1 -> Score without needing a DontDestroyOnLoad object. RaceManager fills this in
// the instant the player finishes (see RaceManager.ComputeFinalStandings), and
// ScoreManager reads it to build the 1st/2nd/3rd... ranking list.
public static class RaceResults
{
    public struct Entry
    {
        public string driverName;   // "You" for the player, "AI 1".."AI 4" for opponents
        public bool isPlayer;
        public bool finished;       // true = actually crossed the line, false = still racing (DNF at the moment the player finished)
        public float totalTime;     // only meaningful when finished == true
        public int lap;             // used to rank unfinished cars against each other
        public int checkpointIndex; // used as a tiebreaker within the same lap
    }

    // Already sorted 1st -> last by the time RaceManager writes it.
    public static List<Entry> FinalStandings = new List<Entry>();

    // NEW: set by RaceManager.EndRace() when the player bails out mid-race via the
    // "END RACE" button instead of actually crossing the finish line. ScoreManager reads
    // this to show the real (possibly DNF) standings instead of faking completed times/
    // rewards like it normally does for a genuine finish, and to mark the result invalid.
    public static bool RaceEndedEarly = false;

    public static void Clear()
    {
        FinalStandings.Clear();
        RaceEndedEarly = false;
    }
}