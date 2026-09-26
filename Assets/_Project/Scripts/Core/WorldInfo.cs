namespace TheDeep.Core
{
    /// <summary>Shared constants about the game world.</summary>
    public static class WorldInfo
    {
        /// <summary>Depth below the surface (metres) of world y = 0, the sub's cabin floor.</summary>
        public const float SurfaceDepth = 1280f;

        public static float DepthAt(float worldY) => SurfaceDepth - worldY;
    }
}
