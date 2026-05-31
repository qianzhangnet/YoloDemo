namespace YoloDemo
{
    internal static class PoseMetadata
    {
        public const float VisibleKeypointConfidence = 0.25f;
        public const float ReliableKeypointConfidence = 0.35f;

        public static readonly int[,] SkeletonPairs =
        {
            {15, 13}, {13, 11}, {16, 14}, {14, 12}, {11, 12}, {5, 11}, {6, 12},
            {5, 6}, {5, 7}, {6, 8}, {7, 9}, {8, 10}, {1, 2}, {0, 1},
            {0, 2}, {1, 3}, {2, 4}, {3, 5}, {4, 6}
        };

        public static readonly int[] LimbColorIndexes =
        {
            9, 9, 9, 9, 7, 7, 7, 0, 0, 0, 0, 0, 16, 16, 16, 16, 16, 16, 16
        };

        public static readonly int[] KeypointColorIndexes =
        {
            16, 16, 16, 16, 16, 0, 0, 0, 0, 0, 0, 9, 9, 9, 9, 9, 9
        };

        public static readonly string[] KeypointNames =
        {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };
    }
}
