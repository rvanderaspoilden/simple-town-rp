using Mirror;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
//  NPC — Server → Client messages
//  Namespace global volontairement (cohérence avec les autres messages Mirror
//  du projet — voir simple-town-rp/CLAUDE.md, section "Namespaces").
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Ordonne au client d'instancier un NPC localement.</summary>
public struct S2C_SpawnNpc : NetworkMessage {
    public int        NpcId;
    public string     RoomId;
    public Vector3    Position;
    public Quaternion Rotation;

    /// <summary>
    /// JSON du <see cref="Style"/> (cf. CharacterStyleSetup.ApplyStyle).
    /// Vide ou null = pas de style (le client laisse l'apparence par défaut du prefab).
    /// On reste sur le même mécanisme JsonUtility que rawCharacterData côté joueur.
    /// </summary>
    public string StyleJson;

    // Identité — constante pour toute la vie du NPC, transmise au spawn uniquement.
    public string FirstName;
    public string LastName;
    public byte   Mood;       // MoodEnum cast en byte

    /// <summary>
    /// Id de la NpcConfig (vide pour un passant sans config). Le client recharge l'asset via
    /// DatabaseManager.GetNpcConfigById pour en dériver le label marchand, le dialogue et la
    /// nature marchande. Constant après le spawn.
    /// </summary>
    public string ConfigId;
}

/// <summary>
/// Mise à jour de transform (snapshot ~10 Hz, throttlée serveur).
/// Le client interpole entre deux snapshots — il NE téléporte PAS sur réception.
/// </summary>
public struct S2C_UpdateNpcTransform : NetworkMessage {
    public int               NpcId;
    public string            RoomId;
    public Vector3           Position;
    public Quaternion        Rotation;
    public Vector3           Velocity;
    public NpcStateType      State;
}

/// <summary>Ordonne au client de détruire un NPC.</summary>
public struct S2C_DestroyNpc : NetworkMessage {
    public int    NpcId;
    public string RoomId;
}

/// <summary>
/// One-shot : le NPC est renversé (ragdoll), effondrement sur place sans projection. L'état
/// persistant « renversé » transite, lui, via
/// <see cref="S2C_UpdateNpcTransform.State"/> = <see cref="NpcStateType.KnockedDown"/>.
/// </summary>
public struct S2C_NpcKnockdown : NetworkMessage {
    public int    NpcId;
    public string RoomId;
}
