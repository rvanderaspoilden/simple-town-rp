using UnityEngine;

namespace Sub_Games.Dream {
    public class SheepController : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private float moveSpeedMin;

        [SerializeField]
        private float moveSpeedMax;

        [SerializeField]
        private float fallSpeed;

        [Header("Only for debug")]
        [SerializeField]
        private float moveSpeed;

        private bool _inArea;
        private bool _jumping;
        private bool _falling;
        private bool _died;

        private Animator _animator;
        private static readonly int Jumping = Animator.StringToHash("Jumping");
        private static readonly int Fail = Animator.StringToHash("Fail");

        private void Awake() {
            this._animator = GetComponent<Animator>();
            this.moveSpeed = Random.Range(this.moveSpeedMin, this.moveSpeedMax);
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

        private void OnTriggerEnter2D(Collider2D other) {
            if (other.CompareTag("Fall Point") && !_jumping) {
                this._died = true;
                this._animator.SetTrigger(Fail);
                GameManager.Instance.AddError();
                Destroy(this.gameObject, 3f);
            } else if(other == GameManager.Instance.StartJumpCollider){
                this._inArea = true;
            } else if (other == GameManager.Instance.StopJumpCollider) {
                this._jumping = false;
                this._animator.SetBool(Jumping, false);
                GameManager.Instance.AddPoint();
            }
        }

        private void OnMouseDown() {
            if (!_inArea) return;

            this._animator.SetBool(Jumping, true);
            this._jumping = true;
        }
    }
}