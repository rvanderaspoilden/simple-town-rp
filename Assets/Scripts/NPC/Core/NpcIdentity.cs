using Sim.Enums;

/// <summary>
/// Identité d'un NPC. Constante pour toute la vie du NPC (du spawn au despawn).
/// Sérialisée dans S2C_SpawnNpc et propagée aux clients.
/// </summary>
public struct NpcIdentity {
    public string    FirstName;
    public string    LastName;
    public MoodEnum  Mood;

    public string FullName => $"{FirstName} {LastName}";

    public static NpcIdentity Empty => new NpcIdentity {
        FirstName = string.Empty,
        LastName  = string.Empty,
        Mood      = MoodEnum.HAPPY
    };
}
