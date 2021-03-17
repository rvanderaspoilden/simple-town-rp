using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sim.Enums;
using Sim.Scriptables;
using Sim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class MoodSelectorUI : MonoBehaviour {
        [Header("Settings")] [SerializeField] private Image arrow;

        [SerializeField] private Vector2 moodSizeDelta;

        [SerializeField] private float openAnimationDuration;

        [SerializeField] private float closeAnimationDuration;

        [SerializeField] private float showMoodAnimationDuration;

        [SerializeField] private float hideMoodAnimationDuration;

        [SerializeField] private float moodAnimationDelay;

        [SerializeField] private MoodButton moodButtonPrefab;

        [SerializeField] private Transform moodContainer;

        private bool moodSelectorOpened;

        private bool isOpening;

        private RectTransform rectTransform;

        private Vector2 initialSizeDelta;

        private float initialAnchorX;

        private Vector2 openedSizeDelta;

        private float openedAnchorX;

        private List<RectTransform> moodsTransforms;

        private readonly Vector3 negativeScale = new Vector3(-1, 1, 1);

        private void Awake() {
            this.rectTransform = GetComponent<RectTransform>();
            this.initialSizeDelta = this.rectTransform.sizeDelta;
            this.initialAnchorX = this.rectTransform.anchoredPosition.x;
        }

        private void OnEnable() {
            MoodButton.OnClick += SelectMood;
        }

        private void OnDisable() {
            MoodButton.OnClick -= SelectMood;
        }

        private void Start() {
            this.moodsTransforms = new List<RectTransform>();
            DatabaseManager.MoodConfigs.ForEach(config => {
                if (config.MoodEnum == NetworkManager.Instance.CharacterData.Mood) return;

                MoodButton moodButton = Instantiate(this.moodButtonPrefab, this.moodContainer);
                moodButton.Setup(config);

                this.moodsTransforms.Add(moodButton.GetComponent<RectTransform>());
            });
        }

        private void SelectMood(MoodButton moodButton) {
            MoodEnum currentMood = RoomManager.LocalCharacter.CharacterData.Mood;
            RoomManager.LocalCharacter.SetMood(moodButton.MoodConfig);
            moodButton.Setup(DatabaseManager.GetMoodConfigByEnum(currentMood));
        }

        public void ToggleMoodSelector() {
            if (this.isOpening) {
                return;
            }

            this.openedSizeDelta = new Vector2(30f + ((moodSizeDelta.x + 20f) * this.moodsTransforms.Count),
                this.rectTransform.sizeDelta.y);

            this.openedAnchorX = this.openedSizeDelta.x / 2f;

            Sequence sequence = DOTween.Sequence();

            this.isOpening = true;

            if (this.moodSelectorOpened) {
                this.moodsTransforms.ForEach(mood => {
                    sequence.Join(mood.DOSizeDelta(Vector2.zero, this.hideMoodAnimationDuration).SetEase(Ease.Linear));
                });

                sequence.Join(this.rectTransform.DOAnchorPosX(this.initialAnchorX, this.closeAnimationDuration)
                    .SetEase(Ease.Linear));
                sequence.Join(this.rectTransform.DOSizeDelta(this.initialSizeDelta, this.closeAnimationDuration)
                    .SetEase(Ease.Linear));

                sequence.OnComplete(() => {
                    this.moodSelectorOpened = false;
                    this.isOpening = false;
                });

                this.arrow.transform.localScale = Vector3.one;
            }
            else {
                sequence.Join(this.rectTransform.DOAnchorPosX(this.openedAnchorX, this.openAnimationDuration)
                    .SetEase(Ease.OutBounce));

                sequence.Join(this.rectTransform.DOSizeDelta(this.openedSizeDelta, this.openAnimationDuration)
                    .SetEase(Ease.OutBounce));

                this.moodsTransforms.ForEach(mood => {
                    sequence.Join(mood.DOSizeDelta(this.moodSizeDelta, this.showMoodAnimationDuration)
                        .SetEase(Ease.OutBounce)
                        .From(Vector2.zero)
                        .SetDelay(this.moodAnimationDelay));
                });

                sequence.OnComplete(() => {
                    this.moodSelectorOpened = true;
                    this.isOpening = false;
                });

                this.arrow.transform.localScale = this.negativeScale;
            }

            sequence.Play();
        }
    }
}