namespace Sim.Missions {
    /// <summary>
    /// Source de missions. Tick côté serveur, peut décider d'appeler
    /// MissionServerManager.Offer(...) pour générer dynamiquement des missions.
    /// Découpe métier : Delivery vs Cleaning vs solo-NPC vs board d'annonces…
    /// </summary>
    public interface IMissionProvider {
        void OnServerStart();
        void OnServerStop();
        void Tick(float dt);
    }
}
