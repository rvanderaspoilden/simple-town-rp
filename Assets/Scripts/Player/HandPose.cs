namespace Sim {
    // Drives the Right Arm / Left Arm animator layers. NONE keeps the arm
    // under the Base layer (no override). Each named value corresponds to a
    // dedicated state in those layers (Carry_Mug, future Carry_Tray, …).
    public enum HandPose {
        NONE = 0,
        MUG  = 1
    }
}
