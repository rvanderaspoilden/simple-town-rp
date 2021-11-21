using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sub_Games.Dream {
    public class SheepController : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private float moveSpeedMin;

        [SerializeField]
        private float moveSpeedMax;

        [SerializeField]
        private float fallSpeed;

        [SerializeField]
        private AudioClip jumpSound;

        [Header("Only for debug")]
        [SerializeField]
        private float moveSpeed;

        private bool _inArea;
        private bool _jumping;
        private bool _falling;
        private bool _died;

        private Animator _animator;
        private AudioSource _audioSource;
        private static readonly int Jumping = Animator.StringToHash("Jumping");
        private static readonly int Fail = Animator.StringToHash("Fail");

        private void Awake() {
            this._animator = GetComponent<Animator>();
            this._audioSource = GetComponent<AudioSource>();
            this.moveSpeed = Random.Range(this.moveSpeedMin, this.moveSpeedMax);
        }

        // Update is called once per frame
        void Update() {
            if (_falling) {
                this.transform.Translate(Vector3.down * Time.deltaTime * this.fallSpeed);
            } else {
                this.transform.Translate(Vector3.right * Time.deltaTime * this.moveSpeed);
            }
        }

        public void StartFalling() {
            this._falling = true;
        }

        private void OnTriggerEnter2D(Collider2D other) {
            if (other.CompareTag("Fall Point") && !_jumping) {
                this._died = true;
                this._animator.SetTrigger(Fail);
                GameManager.Instance.Invoke(nameof(GameManager.AddError), 3f);
                Destroy(this.gameObject, 3f);
            } else if(other == GameManager.Instance.StartJumpCollider){
                this._inArea = true;
            } else if (other == GameManager.Instance.StopJumpCollider) {
                this._jumping = false;
                this._animator.SetBool(Jumping, false);
                Destroy(this.gameObject, 3f);
            }
        }

        private void OnTriggerExit2D(Collider2D other) {
            if(other == GameManager.Instance.StartJumpCollider){
                this._inArea = false;
            }
        }

        private void OnMouseDown() {
            if (!_inArea || this._jumping) return;

            this._animator.SetBool(Jumping, true);
            this._jumping = true;
            this._audioSource.PlayOneShot(this.jumpSound);
            GameManager.Instance.AddPoint();
        }
    }
}