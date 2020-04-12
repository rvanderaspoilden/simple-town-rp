using UnityEngine;
using UnityEngine.AI;

namespace Sim {
    public class Player : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField] private NavMeshAgent agent;

        private void Awake() {
            this.agent = GetComponent<NavMeshAgent>();
        }

        public void SetTarget(Vector3 target) {
            this.agent.SetDestination(target);
        }
    }
}