using System;
using UnityEngine;

namespace Sim {
    public class LoadingManager : MonoBehaviour {
        [Header("Only for debug")]
        [SerializeField] private Animator animator;

        [SerializeField] private bool show;

        public static LoadingManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);

            this.animator = GetComponent<Animator>();
        }

        public void Show() {
            this.show = true;
            this.UpdateAnimation();
        }

        public void Hide() {
            this.show = false;
            this.UpdateAnimation();
        }

        private void UpdateAnimation() {
            this.animator.SetBool("show", this.show);
        }
    }
}