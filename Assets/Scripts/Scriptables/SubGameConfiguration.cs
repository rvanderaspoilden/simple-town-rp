using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New Sub Game", menuName = "Configurations/Sub Game")]
public class SubGameConfiguration : ScriptableObject {
    [SerializeField]
    private SubGameType subGameType;

    [SerializeField]
    private string sceneName;

    [SerializeField]
    private bool hasIntermediateValidatorButton;

    public SubGameType SubGameType => subGameType;

    public string SceneName => sceneName;

    public bool HasIntermediateValidatorButton => hasIntermediateValidatorButton;
}
