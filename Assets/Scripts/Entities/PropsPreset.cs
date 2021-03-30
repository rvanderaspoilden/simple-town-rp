using System;
using UnityEngine;

[Serializable]
public class PropsPreset  {
    [SerializeField]
    private int id;

    [SerializeField]
    private string label;

    [SerializeField]
    private PropsStyle primary;

    [SerializeField]
    private PropsStyle secondary;

    [SerializeField]
    private PropsStyle tertiary;

    public int ID => id;

    public string Label => label;

    public PropsStyle Primary => primary;

    public PropsStyle Secondary => secondary;

    public PropsStyle Tertiary => tertiary;
}
