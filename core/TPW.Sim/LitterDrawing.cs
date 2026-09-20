namespace TPW.Sim
{
    /// <summary>READ: global sprite table entry, 12 bytes (0x800BDCD4): page +0, CLUT +2,
    /// width/height +6/+7, U/V +8/+9. Image data comes from the host's FOLIO 0x1A0 sheet.</summary>
    public readonly record struct LitterSprite(ushort Page, ushort Clut, byte Width, byte Height, byte U, byte V);

    /// <summary>READ: one terrain corner, signed halfwords, in X/height/Y order.</summary>
    public readonly record struct LitterVertex(short X, short Height, short Y, byte U, byte V);

    /// <summary>READ: the textured terrain quad prepared by 0x8006666C, in PSX FT4 corner order.
    /// The host projects the first three corners (RTPT), rejects a negative GTE flag, computes AVSZ3,
    /// rejects unsigned depth >= DepthLimit, then projects/rejects corner D and submits at that depth.
    /// This boundary keeps camera/GPU ownership out of the simulation.</summary>
    public readonly record struct LitterQuad(LitterSprite Sprite, LitterVertex A, LitterVertex B,
        LitterVertex C, LitterVertex D, byte Command, byte Colour, int DepthLimit);

    public interface ILitterDrawWorld
    {
        LitterSprite Sprite(int id);
        /// <summary>READ: 0x80050938(x,y,0), sampled separately at all four corners.</summary>
        short TerrainHeight(short x, short y);
        void SubmitLitterQuad(LitterQuad quad);
    }

    /// <summary>READ: litter is drawn flat on the terrain, not as an upright person sprite.
    /// No rotation, animation, tint by dirtiness, fade or height stored on the piece.</summary>
    public static class LitterDrawing
    {
        /// <summary>READ: 0x80102DE4 = 3, only reader 0x800666A4; half sprite extent is shifted by it.</summary>
        public const int SpriteScaleShift = 3;
        /// <summary>READ: 0x800667F4 (unsigned sltiu 0x7D0).</summary>
        public const int DepthLimit = 2000;
        /// <summary>READ: 0x80066850..68: opaque textured four-point polygon, neutral RGB.</summary>
        public const byte Command = 0x2C;
        public const byte NeutralColour = 0x80;

        public static void Draw(Litter piece, ILitterDrawWorld world)
        {
            var sprite = world.Sprite(piece.SpriteId);
            // ⚠ DO NOT FIX: halve FIRST. Odd dimensions lose their last half-pixel (0x800666B4/C8).
            int halfX = (sprite.Width >> 1) << SpriteScaleShift;
            int halfY = (sprite.Height >> 1) << SpriteScaleShift;
            short left = unchecked((short)(piece.X - halfX));
            short right = unchecked((short)(piece.X + halfX));
            short top = unchecked((short)(piece.Y - halfY));
            short bottom = unchecked((short)(piece.Y + halfY));
            byte u1 = unchecked((byte)(sprite.U + sprite.Width - 1));
            byte v1 = unchecked((byte)(sprite.V + sprite.Height - 1));
            var a = new LitterVertex(left, world.TerrainHeight(left, top), top, sprite.U, sprite.V);
            var b = new LitterVertex(right, world.TerrainHeight(right, top), top, u1, sprite.V);
            var c = new LitterVertex(left, world.TerrainHeight(left, bottom), bottom, sprite.U, v1);
            var d = new LitterVertex(right, world.TerrainHeight(right, bottom), bottom, u1, v1);
            world.SubmitLitterQuad(new LitterQuad(sprite, a, b, c, d, Command, NeutralColour, DepthLimit));
        }
    }
}
