using System;

[Serializable]
public struct Style {
    public CharacterPartStyle hair;
    
    public CharacterPartStyle eyebrow;
    
    public CharacterPartStyle shirt;
    
    public CharacterPartStyle pant;
    
    public CharacterPartStyle shoes;
    
    public float skinColorPercent;
}

[Serializable]
public struct CharacterPartStyle {
    public int idx;

    public float[] color;
}
