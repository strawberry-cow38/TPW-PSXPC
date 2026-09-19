using System;

namespace TPW.Data
{
    /// <summary>The park camera as the game runs it (0x80054D2C, its normal mode), for the port's "game camera".
    ///
    /// ⭐ ALL OF IT FROM THE GAME'S CODE. The camera has no zoom and no tilt. It sits a fixed <see cref="Behind"/>
    /// (0x6E0) world units behind the point it looks at, measured along the ground in the direction it faces, and a
    /// fixed <see cref="Above"/> (0xA10) above the ground under itself (both set once by 0x80054C1C and never
    /// changed), so it always looks down at atan(0xA10 / 0x6E0) ≈ 55.7° from about 12.2 tiles away. It turns only in
    /// quarter turns: a button adds or takes 0x400 from the target yaw, and the yaw eases toward it by an eighth of
    /// the difference per tick of frame time, with a half step to find that difference. The point it looks at chases
    /// the cursor: eight times the distance, clamped to ±0x3FFF, times the frame time over 1024, in 24.8 fixed
    /// point. Its height eases down by an eighth of the gap per frame and rises at once: it never goes below the
    /// ground height under it plus <see cref="Above"/>. It looks at the ground height under ITSELF (eased by an
    /// eighth), not under the point, so on a slope the view tips a little.
    ///
    /// Not read, so not claimed: how fast the game's cursor moves, and how far it may go. The port moves the cursor
    /// with the keys and keeps it on the map.</summary>
    public sealed class ParkCamera
    {
        /// <summary>World units above the ground under the camera (0x80054C1C: 0xA10).</summary>
        public const int Above = 0xA10;
        /// <summary>World units behind the focus, along the ground (0x80054C1C: 0x6E0, scaling (0, 0, 4096)).</summary>
        public const int Behind = 0x6E0;
        /// <summary>One quarter turn, what a turn button adds to the target yaw.</summary>
        public const int QuarterTurn = 0x400;

        /// <summary>Target and current yaw, 4096 to a turn, unwrapped as the game keeps them.</summary>
        public int TargetYaw, Yaw;
        /// <summary>The point looked at, x and z in 24.8 fixed point (0x8010999C, 0x801099A4).</summary>
        public int FocusX, FocusZ;
        /// <summary>Where the focus is heading: the cursor, in world units.</summary>
        public int CursorX, CursorZ;
        /// <summary>The camera's height and the height it looks at, eased (0x801038F0, 0x801099D0).</summary>
        public int EyeY, LookY;
        public int EyeX, EyeZ;
        int _ground;
        bool _started;

        /// <summary>Start looking at (x, z) world units from yaw <paramref name="yaw"/>, heights settled.</summary>
        public void Reset(ParkMap map, int x, int z, int yaw)
        {
            CursorX = x; CursorZ = z; FocusX = x << 8; FocusZ = z << 8;
            TargetYaw = Yaw = yaw;
            Place(map);
            _ground = GroundHeight(map, EyeX, EyeZ);
            EyeY = _ground + Above; LookY = _ground;
            _started = true;
        }

        public void Turn(int quarters) => TargetYaw += QuarterTurn * quarters;

        /// <summary>One park frame of <paramref name="frameTime"/> time units (EntranceFlags.TimeUnitsPerSecond).</summary>
        public void Step(ParkMap map, int frameTime)
        {
            if (!_started) { Reset(map, CursorX, CursorZ, TargetYaw); return; }
            frameTime = Math.Min(frameTime, 0x4000);
            // The yaw: wrap the difference into ±0x800, take a half step to find it, then a full one with it.
            int diff = Wrap(TargetYaw - Yaw);
            int half = ((diff < 0 ? diff + 7 : diff) >> 3) * frameTime >> 13;
            int mid = Wrap(TargetYaw - (Yaw + half));
            Yaw += ((mid < 0 ? mid + 7 : mid) >> 3) * frameTime >> 12;
            // The focus chases the cursor.
            FocusX -= Chase(FocusX, CursorX) * frameTime >> 10;
            FocusZ -= Chase(FocusZ, CursorZ) * frameTime >> 10;
            Place(map);
            // The ground under the camera is re-read only while the camera is over the map (0x800508C8).
            int tx = EyeX >> 8, tz = EyeZ >> 8;
            if (tx >= 0 && tx < map.Width - 1 && tz >= 0 && tz < map.Height - 1) _ground = GroundHeight(map, EyeX, EyeZ);
            int ground = _ground;
            int eye = ground + Above;
            EyeY -= (EyeY - eye) >> 3;
            if (EyeY < eye) EyeY = eye;
            LookY -= (LookY - ground) >> 3;
        }

        int Wrap(int d)
        {
            // The game moves the current yaw by a turn instead of wrapping the difference; the difference is the same.
            while (d < -0x800) { d += 0x1000; Yaw -= 0x1000; }
            while (d > 0x800) { d -= 0x1000; Yaw += 0x1000; }
            return d;
        }

        static int Chase(int focusFixed, int cursor)
        {
            int d = ((focusFixed >> 8) - cursor) * 8;
            return Math.Clamp(d, -0x3FFF, 0x3FFF);
        }

        void Place(ParkMap map)
        {
            int a = Yaw & 0xFFF;
            EyeX = (FocusX >> 8) - (EntranceFlags.Sin(a) * Behind >> 12);
            EyeZ = (FocusZ >> 8) - (EntranceFlags.Cos(a) * Behind >> 12);
        }

        /// <summary>The ground's height at world (x, z), as 0x80050938 gives it: the four corner heights of the tile
        /// (byte +1 × 4, 0 off the map) blended by the position within the tile, x first.</summary>
        public static int GroundHeight(ParkMap map, int x, int z)
        {
            int tx = x >> 8, tz = z >> 8, fx = x & 0xFF, fz = z & 0xFF;
            int H(int cx, int cz) => cx >= 0 && cx < map.Width - 1 && cz >= 0 && cz < map.Height - 1 ? map[cx, cz].HeightUnits : 0;
            int h00 = H(tx, tz), h10 = H(tx + 1, tz), h01 = H(tx, tz + 1), h11 = H(tx + 1, tz + 1);
            int top = h00 + (fx * ((h10 - h00) << 16 >> 8) >> 16);
            int bottom = h01 + (fx * ((h11 - h01) << 16 >> 8) >> 16);
            return (short)(top + (fz * ((bottom - top) << 16 >> 8) >> 16));
        }
    }
}
