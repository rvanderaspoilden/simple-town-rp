using Sim.Enums;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "New Mood", menuName = "Configurations/Mood")]
    public class MoodConfig : ScriptableObject {
        [SerializeField] private new string name;
        [SerializeField] private Sprite sprite;
        [SerializeField] private MoodEnum moodEnum;

        public string Name => name;

        public Sprite Sprite => sprite;

        public MoodEnum MoodEnum => moodEnum;
    }
}