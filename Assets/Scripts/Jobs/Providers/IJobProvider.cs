namespace Sim.Jobs {
    /// <summary>
    /// Source de missions. Tick côté serveur, peut décider d'appeler
    /// JobServerManager.Offer(...) pour générer dynamiquement des missions.
    /// Découpe métier : Delivery vs Cleaning vs solo-NPC vs board d'annonces…
    /// </summary>
    public interface IJobProvider {
        void OnServerStart();
        void OnServerStop();
        void Tick(float dt);
    }
}
