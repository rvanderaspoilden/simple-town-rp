namespace Sim {
    // Drives the Two Hand animator layer (Upper.mask, overrides both arms).
    // NONE keeps the upper body under the Base layer (no override). Add
    // values (GUN, …) when their 2H clips exist.
    public enum TwoHandPose {
        NONE = 0,
        BOX  = 1
    }
}
