using UnityEngine;

/// <summary>
/// Snapshot serveur d'un NPC. Mutable, vit dans NpcServerManager.
/// Stocke aussi les "lastSent*" pour throttler le réseau (delta filtering).
/// </summary>
public class NpcServerState {
    public int    NpcId;
    public string RoomId;

    /// <summary>JSON du Style (cohérent avec PlayerController.rawCharacterData).</summary>
    public string StyleJson;

    /// <summary>Identité (nom, mood) — constante après le spawn.</summary>
    public NpcIdentity Identity;

    /// <summary>Id de la NpcConfig (vide pour un passant) — constant après le spawn. Le client
    /// recharge l'asset via DatabaseManager.GetNpcConfigById pour le label marchand et le dialogue.</summary>
    public string ConfigId;

    public Vector3      Position;
    public Quaternion   Rotation;
    public Vector3      Velocity;
    public NpcStateType State;

    // Dernier état réellement broadcasté → utilisé pour décider si on renvoie un update.
    public Vector3      LastSentPosition;
    public Quaternion   LastSentRotation;
    public NpcStateType LastSentState;
    public bool         EverSent;
}
