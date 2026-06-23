using System;
using Sim;
using UnityEngine;
using UnityEngine.AI;

namespace AI.States {
    /// <summary>
    /// « Poser » un item tenu : le personnage marche jusqu'à <c>standPoint</c> (point accessible
    /// le plus proche de l'emplacement choisi) puis, à l'arrivée, déclenche <c>onArrive</c> (envoi
    /// de la requête réseau qui détache l'item de la main et le pose à l'emplacement). Tant qu'il
    /// n'est pas arrivé, l'item reste en main.
    ///
    /// IMPORTANT : ce state pilote LUI-MÊME le NavMeshAgent (SetDestination + feed animator) au lieu
    /// d'appeler <see cref="PlayerController.MoveTo"/> — MoveTo fait un SetState(moveState) qui
    /// remplacerait immédiatement ce state. L'annulation est automatique : tout autre state qui prend
    /// la main (clic-déplacement → moveState, interaction → interactState…) remplace celui-ci →
    /// <see cref="OnExit"/> s'exécute sans complétion (<c>_completed == false</c>) → <c>onCancel</c>,
    /// la requête de pose n'est jamais envoyée (l'item reste en main).
    /// </summary>
    public class CharacterPoser : IState {
        // Tolérance d'arrivée si l'agent n'a pas de stoppingDistance significatif.
        private const float ArriveTolerance = 0.2f;

        private readonly PlayerController player;
        private readonly Vector3 standPoint;
        private readonly Action onArrive;
        private readonly Action onCancel;

        private bool _completed;

        public CharacterPoser(PlayerController player, Vector3 standPoint, Action onArrive, Action onCancel = null) {
            this.player = player;
            this.standPoint = standPoint;
            this.onArrive = onArrive;
            this.onCancel = onCancel;
        }

        public void OnEnter() {
            _completed = false;
            this.player.PlayerState = PlayerState.MOVING;
            this.player.NavMeshAgent.SetDestination(this.standPoint);
        }

        public void Tick() {
            if (_completed) return;

            NavMeshAgent agent = this.player.NavMeshAgent;

            // Le chemin est calculé sur une frame après SetDestination : on patiente.
            if (agent.pathPending) return;

            // Marqueur de destination + animation de marche, même recette que CharacterMove.
            MarkerController.Instance.ShowAt(agent.pathEndPosition);

            Vector3 desired = agent.desiredVelocity;
            this.player.Animator.SetVelocity(desired.magnitude);

            Vector3 flatDesired = new Vector3(desired.x, 0f, desired.z);
            if (flatDesired.sqrMagnitude > 0.0001f) {
                this.player.transform.rotation = Quaternion.LookRotation(flatDesired.normalized);
            }

            // Arrivée : plus de chemin à parcourir et agent stoppé.
            float arriveDist = Mathf.Max(agent.stoppingDistance, ArriveTolerance);
            if (agent.remainingDistance <= arriveDist
                && (!agent.hasPath || agent.velocity.sqrMagnitude < 0.01f)) {
                _completed = true;
                this.player.Animator.SetVelocity(0f);
                this.onArrive?.Invoke();   // envoie C2S_PoseHeldItem (détache + pose l'item)
                this.player.Idle();        // → stateMachine swap idle → OnExit cleanup
            }
        }

        public void OnExit() {
            this.player.NavMeshAgent.ResetPath();
            this.player.Animator.SetVelocity(0f);
            MarkerController.Instance.Hide();

            // Sortie sans complétion = annulation (déplacement / interaction) : la requête de pose
            // n'a pas été envoyée, l'item reste en main.
            if (!_completed) this.onCancel?.Invoke();
        }
    }
}
