namespace TheDeep.Progression
{
    public enum UpgradeType { RopeLength, WalkieRange, SwimSpeed, DepthRating, SuitStrength }

    /// <summary>Names, costs and effects of the permanent upgrades (3 levels each).</summary>
    public static class UpgradeCatalog
    {
        public const int Count = 5;
        public const int MaxLevel = 3;

        static readonly string[] Names = { "Rope Length", "Walkie Range", "Suit: Swim Speed", "Suit: Depth Rating", "Suit: Strength" };
        static readonly int[][] Costs =
        {
            new[] { 150, 300, 500 },  // rope
            new[] { 120, 250, 450 },  // walkie
            new[] { 100, 220, 400 },  // swim
            new[] { 200, 400, 700 },  // depth
            new[] { 180, 360, 600 },  // strength
        };

        public static string Name(UpgradeType type) => Names[(int)type];

        /// <summary>Cost to go from <paramref name="currentLevel"/> to the next level; -1 if maxed.</summary>
        public static int Cost(UpgradeType type, int currentLevel) =>
            currentLevel >= MaxLevel ? -1 : Costs[(int)type][currentLevel];

        // ---- effects

        public static float RopeLength(int level) => 60f + 20f * level;
        /// <summary>Multiplier on walkie-talkie range.</summary>
        public static float SignalRange(int level) => 1f + 0.35f * level;
        public static float SwimSpeed(int level) => 1f + 0.15f * level;
        public static int DepthRating(int level) => 1400 + 380 * level;
        public static int Strength(int level) => 100 + 25 * level;

        /// <summary>Human-readable effect of a level, for the Balance app.</summary>
        public static string Describe(UpgradeType type, int level) => type switch
        {
            UpgradeType.RopeLength => $"{RopeLength(level):0} M",
            UpgradeType.WalkieRange => $"{SignalRange(level) * 100f:0}% RANGE",
            UpgradeType.SwimSpeed => $"{SwimSpeed(level) * 100f:0}% SPEED",
            UpgradeType.DepthRating => $"{DepthRating(level)} M",
            _ => $"{Strength(level)}% GRIP",
        };
    }
}
