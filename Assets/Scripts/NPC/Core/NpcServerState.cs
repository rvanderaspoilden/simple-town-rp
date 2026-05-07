using UnityEngine;

/// <summary>
/// Snapshot serveur d'un NPC. Mutable, vit dans NpcServerManager.
/// Stocke aussi les "lastSent*" pour throttler le réseau (delta filtering).
/// </summary>
public class NpcServerState {
    public int    NpcId;
    public string PrefabId;
    public string RoomId;

    /// <summary>JSON du Style (cohérent avec PlayerController.rawCharacterData).</summary>
    public string StyleJson;

    /// <summary>Identité (nom, mood) — constante après le spawn.</summary>
    public NpcIdentity Identity;

    public Vector3           Position;
    public Quaternion        Rotation;
    public Vector3           Velocity;
    public NpcAnimationState AnimationState;

    // Dernier état réellement broadcasté → utilisé pour décider si on renvoie un update.
    public Vector3           LastSentPosition;
    public Quaternion        LastSentRotation;
    public NpcAnimationState LastSentAnimationState;
    public bool              EverSent;
}
