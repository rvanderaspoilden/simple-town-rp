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

        private GenericPool<SheepController> _linkedPool;

        private Animator _animator;
        private AudioSource _audioSource;
        private SpriteRenderer _spriteRenderer;
        private static readonly int Jumping = Animator.StringToHash("Jumping");
        private static readonly int Fail = Animator.StringToHash("Fail");

        private void Awake() {
            this._animator = GetComponent<Animator>();
            this._audioSource = GetComponent<AudioSource>();
            this._spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // Update is called once per frame
        void Update() {
            if (_falling) {
                this.transform.Translate(Vector3.down * Time.deltaTime * this.fallSpeed);
            } else if(!_died){
                this.transform.Translate(Vector3.right * Time.deltaTime * this.moveSpeed);
            }
        }

        public void StartFalling() {
            this._falling = true;
        }

        public void LinkPool(GenericPool<SheepController> pool) {
            this._linkedPool = pool;
        }

        public void ResetState() {
            this._died = false;
            this._falling = false;
            this._jumping = false;
            this._inArea = false;
            this._animator.SetBool(Jumping, false);
            this.Freeze();
        }

        public void RandomizeSpeed() {
            this.moveSpeed = Random.Range(this.moveSpeedMin, this.moveSpeedMax);
        }

        public void Freeze() {
            this.moveSpeed = 0;
        }

        public SpriteRenderer SpriteRenderer => _spriteRenderer;

        private void OnTriggerEnter2D(Collider2D other) {
            if (other.CompareTag("Fall Point") && !_jumping) {
                this._died = true;
                this._animator.SetTrigger(Fail);
                GameManager.Instance.Invoke(nameof(GameManager.AddError), 3f);
            } else if(other == GameManager.Instance.StartJumpCollider){
                this._inArea = true;
            } else if (other == GameManager.Instance.StopJumpCollider) {
                this._jumping = false;
                this._animator.SetBool(Jumping, false);
            } else if (GameManager.Instance.DestroyColliders.Contains(other)) {
                this._linkedPool.Release(this);
            }
        }

        private void OnTriggerExit2D(Collider2D other) {
            Debug.Log(other.name);
            if(other == GameManager.Instance.StartJumpCollider){
                this._inArea = false;
                Debug.Log("Not in area");
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