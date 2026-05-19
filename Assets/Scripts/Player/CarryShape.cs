namespace Sim {
    // Hand-agnostic carry style declared on an ItemConfig. The resolver maps
    // it to the relevant animator layer: 1H shapes (MUG, TRAY, …) drive
    // RightHandPose / LeftHandPose, 2H shapes (BOX, GUN, …) drive
    // TwoHandPose. The HandleType on ItemConfig decides which side of that
    // dispatch applies. Add new values once their clips exist and extend
    // HandPoseResolver.Resolve accordingly.
    public enum CarryShape {
        NONE = 0,
        MUG  = 1,
        BOX  = 2
    }
}
