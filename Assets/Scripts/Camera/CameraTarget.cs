using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sim {
    public class CameraTarget : MonoBehaviour {

        private Transform target;

        private bool isSmoothing;

        private void OnEnable() {
            if (target) {
                this.isSmoothing = true;
                this.transform.DOMove(target.position, 0.5f).OnComplete(() => this.isSmoothing = false);
            }
        }

        // Update is called once per frame
        void Update() {
            if (target && !this.isSmoothing) {
                this.transform.position = target.position;
            }
        }

        public void SetTarget(Transform target) {
            this.target = target;
        }

        public Transform GetTarget() {
            return this.target;
        }
    }
}
