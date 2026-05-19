namespace Sim {
    public static class HandPoseResolver {
        public static CarryPose Resolve(ItemBehaviour left, ItemBehaviour right) {
            if (left == null && right == null) return CarryPose.NONE;

            // Item-specific override wins (right hand precedence on tie).
            if (right != null && right.Configuration != null && right.Configuration.Pose != CarryPose.NONE) {
                return right.Configuration.Pose;
            }
            if (left != null && left.Configuration != null && left.Configuration.Pose != CarryPose.NONE) {
                return left.Configuration.Pose;
            }

            bool rightTwoHand = right != null && right.Configuration != null && right.Configuration.HandleType == ItemHandleType.TWO_HAND;
            bool leftTwoHand  = left  != null && left.Configuration  != null && left.Configuration.HandleType  == ItemHandleType.TWO_HAND;
            if (rightTwoHand || leftTwoHand) return CarryPose.TWO_HAND;

            if (right != null && left != null) return CarryPose.ONE_HAND_BOTH;
            if (right != null) return CarryPose.ONE_HAND_RIGHT;
            return CarryPose.ONE_HAND_LEFT;
        }
    }
}
