using System;

[Serializable]
public struct Style {
    public CharacterPartStyle hair;
    
    public CharacterPartStyle eyebrow;
    
    public CharacterPartStyle shirt;
    
    public CharacterPartStyle pant;
    
    public CharacterPartStyle shoes;
    
    public float skinColorPercent;

    public Gender gender;
}

[Serializable]
public struct CharacterPartStyle {
    public int idx;

    public float[] color;
}
