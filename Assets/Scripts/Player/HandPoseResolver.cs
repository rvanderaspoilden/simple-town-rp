namespace Sim {
    public struct ResolvedPose {
        public HandPose    Right;
        public HandPose    Left;
        public TwoHandPose TwoHand;
    }

    public static class HandPoseResolver {
        // Maps the runtime hand occupation + each item's CarryShape to the
        // animator layers. A 1H item drives RightHandPose or LeftHandPose
        // depending on which hand it's in; a 2H item drives TwoHandPose and
        // suppresses per-arm overrides. CarryShape.NONE keeps the relevant
        // layer at "Empty" — Base layer locomotion shows through.
        public static ResolvedPose Resolve(ItemBehaviour left, ItemBehaviour right) {
            var result = new ResolvedPose();

            bool rightTwoHand = right != null && right.Configuration != null
                && right.Configuration.HandleType == ItemHandleType.TWO_HAND;
            bool leftTwoHand  = left  != null && left.Configuration  != null
                && left.Configuration.HandleType  == ItemHandleType.TWO_HAND;

            if (rightTwoHand || leftTwoHand) {
                ItemConfig cfg = (rightTwoHand ? right : left).Configuration;
                result.TwoHand = MapTwoHand(cfg.PoseShape);
                return result;
            }

            if (right != null && right.Configuration != null) {
                result.Right = MapHand(right.Configuration.PoseShape);
            }
            if (left != null && left.Configuration != null) {
                result.Left = MapHand(left.Configuration.PoseShape);
            }
            return result;
        }

        private static HandPose MapHand(CarryShape shape) {
            switch (shape) {
                case CarryShape.MUG: return HandPose.MUG;
                default:             return HandPose.NONE;
            }
        }

        private static TwoHandPose MapTwoHand(CarryShape shape) {
            switch (shape) {
                case CarryShape.BOX: return TwoHandPose.BOX;
                default:             return TwoHandPose.NONE;
            }
        }
    }
}
