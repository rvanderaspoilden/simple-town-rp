using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Sub_Games.Dream {
    public class GameManager : AbstractSubGameManager {
        [Header("Settings")]
        [SerializeField]
        private Collider2D startJumpCollider;

        [SerializeField]
        private Collider2D stopJumpCollider;

        [SerializeField]
        private List<Collider2D> destroyColliders;

        [SerializeField]
        private TextMeshProUGUI counterTxt;

        [SerializeField]
        private SheepController sheepPrefab;

        [SerializeField]
        private List<Transform> spawners;

        [SerializeField]
        private Transform sheepContainer;

        [Header("Debug")]
        [SerializeField]
        private int sheepCount;

        [SerializeField]
        private int failCount;

        [SerializeField]
        private List<SheepController> _waveSheeps;

        private GenericPool<SheepController> _sheepPool;

        public static GameManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
                this._sheepPool = new GenericPool<SheepController>(CreateNewSheep, OnGetSheep, OnReleaseSheep);
                this._waveSheeps = new List<SheepController>();
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

        #region Pool Functions

        private SheepController CreateNewSheep() {
            SheepController sheep = Instantiate(this.sheepPrefab, this.sheepContainer);
            sheep.LinkPool(this._sheepPool);
            return sheep;
        }

        private void OnGetSheep(SheepController sheepController) {
            sheepController.gameObject.SetActive(true);
        }

        private void OnReleaseSheep(SheepController sheepController) {
            sheepController.ResetState();
            sheepController.gameObject.SetActive(false);
            this._waveSheeps.Remove(sheepController);
        }

        #endregion

        public List<Collider2D> DestroyColliders => destroyColliders;

        private IEnumerator WaveCoroutine() {
            this._gameStarted = true;
            float totalSheepToSpawn = 0;

            while (!this._gameStopped) {
                totalSheepToSpawn++;

                int numberOfWaves = Mathf.CeilToInt(totalSheepToSpawn / this.spawners.Count);
                float remainingSheepsToSpawn = totalSheepToSpawn;

                for (int w = 0; w < numberOfWaves; w++) {
                    int[] availableSpawnIdx = this.spawners.Select(x => this.spawners.IndexOf(x)).ToArray();

                    
                    float numberOfSheepToSpawn = remainingSheepsToSpawn <= this.spawners.Count ? remainingSheepsToSpawn : this.spawners.Count;
                    remainingSheepsToSpawn -= numberOfSheepToSpawn;

                    for (int i = 0; i < numberOfSheepToSpawn; i++) {
                        int spawnerIdx = availableSpawnIdx[Random.Range(0, availableSpawnIdx.Length)];

                        ArrayUtility.Remove(ref availableSpawnIdx, spawnerIdx);

                        Transform spawner = this.spawners[spawnerIdx];
                        SheepController sheep = this._sheepPool.Get();
                        sheep.transform.position = spawner.transform.position;
                        sheep.SpriteRenderer.sortingOrder = spawnerIdx + 1;

                        sheep.Invoke(nameof(SheepController.RandomizeSpeed), Random.Range(0f + (3f * w), 3f + (3f * w)));

                        this._waveSheeps.Add(sheep);
                    }
                }

                while (this._waveSheeps.Count > 0) {
                    yield return null;
                }
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