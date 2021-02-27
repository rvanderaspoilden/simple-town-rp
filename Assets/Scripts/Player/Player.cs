using System;
using System.Linq;
using Photon.Pun;
using Sim.Building;
using Sim.Entities;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using UnityEngine.AI;

namespace Sim {
    public class Player : MonoBehaviourPunCallbacks {
        [Header("Settings")] [SerializeField] private Transform headTargetForCamera;

        [Header("Only for debug")] [SerializeField]
        private NavMeshAgent agent;

        [SerializeField] private StateType state;

        [SerializeField] private Props propsTarget;

        private PlayerAnimator playerAnimator;

        private new Rigidbody rigidbody;

        private CharacterData characterData;

        public delegate void StateChanged(Player player, StateType state);

        public static event StateChanged OnStateChanged;

        public delegate void CharacterDataChanged(CharacterData characterData);

        public static event CharacterDataChanged OnCharacterDataChanged;

        private void Awake() {
            this.agent = GetComponent<NavMeshAgent>();
            this.rigidbody = GetComponent<Rigidbody>();
            this.playerAnimator = GetComponent<PlayerAnimator>();

            PhotonNetwork.AddCallbackTarget(this);
        }

        private void Start() {
            if (!this.photonView.IsMine) {
                this.agent.enabled = false;
                this.rigidbody.useGravity = false;
            }
        }

        private void OnDestroy() {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Update() {
            if (!this.photonView.IsMine) return;

            if (this.agent.remainingDistance > this.agent.stoppingDistance) {
                MarkerController.Instance.ShowAt(this.agent.pathEndPosition);

                if (this.propsTarget && this.CanInteractWith(this.propsTarget)) {
                    HUDManager.Instance.DisplayContextMenu(true,
                        CameraManager.camera.WorldToScreenPoint(this.propsTarget.transform.position), this.propsTarget);
                    this.propsTarget = null;
                    this.agent.ResetPath();
                }
            }
            else if (!this.agent.hasPath && MarkerController.Instance.IsActive()) {
                this.agent.ResetPath();

                MarkerController.Instance.Hide();
            }

            this.playerAnimator.SetVelocity(this.agent.velocity.magnitude);
        }

        public CharacterData CharacterData {
            get => characterData;
            set {
                characterData = value;
                this.playerAnimator.SetMood((int)characterData.Mood);
                OnCharacterDataChanged?.Invoke(characterData);
            }
        }

        public bool CanInteractWith(Props propsToInteract) {
            float maxRange = propsToInteract.GetConfiguration().GetRangeToInteract();
            Vector3 origin = Vector3.Scale(propsToInteract.transform.position, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(this.transform.position, new Vector3(1, 0, 1));

            if (propsToInteract.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            Vector3 dir = propsToInteract.transform.position - this.GetHeadTargetForCamera().position;
            RaycastHit hit;
            if (Physics.Raycast(this.GetHeadTargetForCamera().position, dir, out hit)) {
                Props hitProps = hit.collider.GetComponentInParent<Props>();

                if ((hitProps && hitProps.GetType() == typeof(Wall)) || !hitProps) {
                    return false;
                }

                return hitProps.Equals(propsToInteract);
            }

            return false;
        }

        public bool CanInteractWith(Props propsToInteract, Vector3 hitPoint) {
            float maxRange = propsToInteract.GetConfiguration().GetRangeToInteract();
            Vector3 origin = Vector3.Scale(hitPoint, new Vector3(1, 0, 1));
            Vector3 target = Vector3.Scale(this.transform.position, new Vector3(1, 0, 1));

            if (propsToInteract.GetActions()?.Length <= 0 || Mathf.Abs(Vector3.Distance(origin, target)) > maxRange) {
                return false;
            }

            Vector3 dir = hitPoint - this.GetHeadTargetForCamera().position;
            RaycastHit hit;
            if (Physics.Raycast(this.GetHeadTargetForCamera().position, dir, out hit)) {
                Props hitProps = hit.collider.GetComponentInParent<Props>();

                if ((hitProps && hitProps.GetType() == typeof(Wall)) || !hitProps) {
                    return false;
                }

                return hitProps.Equals(propsToInteract);
            }

            return false;
        }

        public void SetState(StateType stateType) {
            Debug.Log($"Player state changed from {this.state} to {stateType}");
            this.state = stateType;
            OnStateChanged?.Invoke(this, stateType);
        }

        public void SetMood(MoodConfig moodConfig) {
            this.characterData.Mood = moodConfig.MoodEnum;
            this.playerAnimator.SetMood((int) moodConfig.MoodEnum);
            OnCharacterDataChanged?.Invoke(this.characterData);
        }

        public StateType GetState() {
            return this.state;
        }

        public void SetTarget(Vector3 targetPoint, Props props) {
            this.agent.SetDestination(targetPoint);
            this.propsTarget = props;
        }

        public Transform GetHeadTargetForCamera() {
            return this.headTargetForCamera;
        }
    }
}