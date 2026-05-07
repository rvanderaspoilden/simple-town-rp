using UnityEngine;

/// <summary>
/// Abstraction commune entre le joueur (PlayerController) et les NPC (NpcAIController).
/// Permet aux systèmes côté serveur (sièges, portes, triggers) de manipuler n'importe
/// quel "Character" sans hardcoder de "if player / if npc".
///
/// Encodage de l'OccupantId — historiquement le SeatState stocke des uint (netId joueur).
/// Pour ne pas casser le wire format, les NPC encodent leur id dans la moitié haute :
///     - 0                 → emplacement vide (inchangé)
///     - 1 .. 0x7FFFFFFF   → netId d'un joueur
///     - 0x8000_0000 | id  → npcId d'un NPC
///
/// Helpers : <see cref="CharacterEntityIds"/>.
/// </summary>
public interface ICharacterEntity {
    /// <summary>Id stable utilisable dans tout payload uint (SeatState, etc.).</summary>
    uint OccupantId { get; }

    /// <summary>True pour un NPC (le bit 31 de OccupantId est mis), false pour un joueur.</summary>
    bool IsNpc { get; }

    /// <summary>Transform du Character (jamais null tant que l'entité vit).</summary>
    Transform Transform { get; }
}

/// <summary>
/// Helpers pour encoder/décoder un OccupantId.
/// Un joueur fournit directement son netId (bit 31 toujours 0 en pratique).
/// Un NPC fournit <see cref="EncodeNpc"/> sur son npcId.
/// </summary>
public static class CharacterEntityIds {
    public const uint NpcFlag = 0x8000_0000u;

    public static uint EncodeNpc(int npcId)   => NpcFlag | (uint)npcId;
    public static bool IsNpc(uint occupantId) => (occupantId & NpcFlag) != 0u && occupantId != 0u;
    public static int  DecodeNpc(uint occupantId) => (int)(occupantId & ~NpcFlag);
}
