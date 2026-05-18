using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim.SubGames {
    // Destroy this EventSystem when the scene is loaded additively (another EventSystem is already active).
    [RequireComponent(typeof(UnityEngine.EventSystems.EventSystem))]
    public class AdditiveSceneEventSystemGuard : MonoBehaviour {
        private void Awake() {
            if (gameObject.scene != SceneManager.GetActiveScene()) {
                Destroy(gameObject);
            }
        }
    }
}
