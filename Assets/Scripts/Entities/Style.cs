using System;

[Serializable]
public struct Style {
    public CharacterPartStyle hair;
    
    public CharacterPartStyle shirt;
    
    public CharacterPartStyle pant;
    
    public CharacterPartStyle shoes;
    
    public float[] skinColor;
}

[Serializable]
public struct CharacterPartStyle {
    public int idx;

    public float[] color;
}
