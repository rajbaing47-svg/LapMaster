// Shared list of AI racer names so opponents look like real drivers instead of
// "AI 1", "AI 2"... Used by RaceManager (actual races) and ScoreManager
// (fake/test standings when useRandomTestDataIfEmpty is on).
public static class AIRacerNames
{
    public static readonly string[] Names = new string[]
    {
        "Mika Salonen",
        "Diego Alvarez",
        "Lucas Ferreira",
        "Kenji Watanabe",
        "Ethan Cole",
        "Marco Rossi",
        "Jonas Berg",
        "Ryan Walker",
        "Alexei Volkov",
        "Tomas Novak",
    };

    // Returns a name by index, wrapping around and appending a number if there are
    // more AI cars in a race than names in the list (e.g. 11th opponent onward).
    public static string Get(int index)
    {
        if (Names == null || Names.Length == 0) return "AI " + (index + 1);

        string baseName = Names[index % Names.Length];
        int lap = index / Names.Length;
        return lap == 0 ? baseName : $"{baseName} {lap + 1}";
    }
}