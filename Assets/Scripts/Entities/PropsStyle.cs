using System;
using UnityEngine;

[Serializable]
public class PropsStyle {
    [SerializeField]
    private bool enabled;
    
    [SerializeField]
    private Color color;

    [SerializeField]
    private Material material;

    public Color Color => color;

    public Material Material => material;

    public bool Enabled => enabled;
}