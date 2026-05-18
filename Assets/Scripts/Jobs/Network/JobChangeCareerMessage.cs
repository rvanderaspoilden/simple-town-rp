using Mirror;

/// <summary>
/// Client → Server. Le joueur a postulé ou démissionné via l'app Carrière.
/// `newJob = -1` → démission (currentJob → null côté backend). Sinon valeur
/// d'une JobCategory. Le serveur fait l'upsert dans la table character_jobs,
/// met à jour characters.current_job, et rebroadcast la CharacterData.
/// </summary>
public struct JobChangeCareerMessage : NetworkMessage {
    public int newJob;
}
