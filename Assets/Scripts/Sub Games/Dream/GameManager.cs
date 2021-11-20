using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sub_Games.Dream {
    public class GameManager : AbstractSubGameManager {
        [Header("Settings")]
        [SerializeField]
        private Collider2D startJumpCollider;

        [SerializeField]
        private Collider2D stopJumpCollider;

        [SerializeField]
        private TextMeshProUGUI counterTxt;

        [SerializeField]
        private SheepController sheepPrefab;

        [SerializeField]
        private List<Transform> spawners;

        [SerializeField]
        private float waveInterval;

        [Header("Debug")]
        [SerializeField]
        private int sheepCount;

        [SerializeField]
        private int failCount;

        public static GameManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        public override void StartGame() {
            base.StartGame();
            StartCoroutine(this.WaveCoroutine());
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.S)) {
                this.StartGame();
            }
        }

        private IEnumerator WaveCoroutine() {
            this._gameStarted = true;

            while (true) {
                for (int i = 0; i < 3; i++) {
                    Transform spawner = this.spawners[Random.Range(0, this.spawners.Count)];
                    Instantiate(this.sheepPrefab, spawner.position, Quaternion.identity);
                }

                yield return new WaitForSeconds(this.waveInterval);
            }
        }

        public Collider2D StartJumpCollider => startJumpCollider;

        public Collider2D StopJumpCollider => stopJumpCollider;

        public void AddPoint() {
            this.sheepCount++;
            this.counterTxt.text = this.sheepCount.ToString();
            this.CheckState();
        }

        public void AddError() {
            this.failCount++;
            this.CheckState();
        }

        private void CheckState() {
            if (failCount >= 3) {
                this.StopGame();
            }
        }
    }
}