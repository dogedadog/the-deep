using System;
using TheDeep.Core;

namespace TheDeep.Progression
{
    // APPEND ONLY - save files store levels by this enum's numeric value.
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

        // Crush depth per level: 50/60/70/80 m below dive stations 1-4 (1280/1580/1980/2410 m), so the
        // end of a long rope is dangerous until the suit is upgraded, while the terrace and the shaft
        // floor (2480 m) stay safe at the level their station needs.
        static readonly int[] Ratings = { 1330, 1640, 2050, 2490 };

        public static string Name(UpgradeType type) => Names[(int)type];

        /// <summary>Cost to go from <paramref name="currentLevel"/> to the next level; -1 if maxed.</summary>
        public static int Cost(UpgradeType type, int currentLevel) =>
            currentLevel >= MaxLevel ? -1 : Costs[(int)type][currentLevel];

        /// <summary>False for upgrades nothing uses yet: the Balance app lists them but won't sell them.</summary>
        public static bool IsAvailable(UpgradeType type) =>
            type != UpgradeType.SuitStrength; // enable once step 5 (creatures) uses suit strength

        // ---- effects

        public static float RopeLength(int level) => 60f + 20f * level;
        /// <summary>Multiplier on walkie-talkie range.</summary>
        public static float SignalRange(int level) => 1f + 0.35f * level;
        public static float SwimSpeed(int level) => 1f + 0.15f * level;
        public static int DepthRating(int level) => Ratings[Math.Clamp(level, 0, Ratings.Length - 1)];
        public static int Strength(int level) => 100 + 25 * level;

        /// <summary>Human-readable effect of a level, for the Balance app.</summary>
        public static string Describe(UpgradeType type, int level) => type switch
        {
            UpgradeType.RopeLength => $"{RopeLength(level):0} M",
            UpgradeType.WalkieRange => $"{SignalModel.DeadRange * SignalRange(level):0} M RANGE",
            UpgradeType.SwimSpeed => $"{SwimSpeed(level) * 100f:0}% SPEED",
            UpgradeType.DepthRating => $"{DepthRating(level)} M",
            _ => $"{Strength(level)}% GRIP",
        };
    }
}
