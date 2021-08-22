using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Help Configuration", menuName = "Configurations/Help Config")]
public class HelpConfig : ScriptableObject {
    [SerializeField]
    private string context;

    [SerializeField]
    private List<GameObject> orderedContent;

    public List<GameObject> OrderedContent => orderedContent;
}