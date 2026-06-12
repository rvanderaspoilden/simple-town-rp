using Mirror;

/// <summary>
/// Client → Server. Le joueur a postulé ou démissionné via l'app Carrière.
/// `newProfessionId = ""` → démission (current_profession_id → null côté backend).
/// Sinon un ProfessionConfig.id. Le serveur fait l'upsert dans character_jobs,
/// met à jour characters.current_profession_id, et rebroadcast la CharacterData.
/// </summary>
public struct MissionChangeCareerMessage : NetworkMessage {
    public string newProfessionId;
}
