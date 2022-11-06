using System.Collections.Generic;
using Enums;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

[CreateAssetMenu(fileName = "New Build Area", menuName = "Configurations/Build Area")]
public class BuildAreaConfig : ScriptableObject {
    [SerializeField]
    private BuildArea prefab;

    [SerializeField]
    private List<BuildingConfig> buildings;

    [SerializeField]
    private BuildAreaType areaType;

    [SerializeField]
    private List<Action> actions;

    [SerializeField]
    private float rangeToInteract;

    public List<BuildingConfig> Buildings => buildings;

    public BuildAreaType AreaType => areaType;

    public BuildArea Prefab => prefab;

    public List<Action> Actions => actions;

    public float RangeToInteract => rangeToInteract;
}