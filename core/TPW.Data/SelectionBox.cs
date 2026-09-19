using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The highlight the park draws round the attraction under the cursor: a glowing, breathing box.
    ///
    /// ⭐ ALL OF IT FROM THE GAME'S CODE. Not an outline and not a model: 0x8001A70C builds it every frame out of
    /// gouraud quads (POLY_G4) drawn additively (each followed by a dummy primitive that sets the draw mode to
    /// tpage 0x20, 0x8001A400: B + F).
    /// <list type="bullet">
    /// <item>A ring of eight points round the footprint (corners and mid-edges), starting at the object's base
    /// height. Four more rings above it, at a quarter, a half, three quarters and all of the box's height, each
    /// pushed out from the footprint by sin(45°, 90°, 135°, 180°) × (200 − pulse) world units: the box bulges
    /// in the middle and closes back to the footprint at the top.</item>
    /// <item>The height is (object height in tiles + 1) × 256 + pulse, the pulse |sin(timer × 5 / 512)| × 256
    /// (the timer is the frame time the park has run, 0x801026B8), so the box grows taller and thinner and
    /// back, about 1.2 times a second.</item>
    /// <item>Its colour is <see cref="Glow"/>, orange (116, 72, 4), scaled by the fade (0..128, / 128). The
    /// bottom ring and the top ring glow fully, the middle ring not at all (|angle − 90°| / 90°); on every ring
    /// the corners glow and the mid-edges an eighth as much, so the sides fade toward their middles and the box
    /// reads as rounded. Added onto green grass, the orange shows as pale yellow-green.</item>
    /// <item>Each band's eight sides are drawn only when they face the camera (NCLIP on the first three
    /// corners, 0x800C4BD4); a lid of four quads from the top ring's corners (glowing) to its mid-edges and the
    /// footprint's centre (<see cref="Lid"/>, black) is always drawn (0x8001A490).</item>
    /// </list>
    /// The object's height is its model's extent in y at its current frame, from the ground up, whole tiles
    /// (slot 0x6C, 0x80062C6C: (top − max(bottom, 0)) >> 8); the park gate's is a byte of its record.</summary>
    public static class SelectionBox
    {
        /// <summary>The two colours (0x801026C4, 0x801026C8), never written at run time.</summary>
        public static readonly (byte R, byte G, byte B) Lid = (0, 0, 0), Glow = (116, 72, 4);

        /// <summary>A fade at full strength.</summary>
        public const int FullFade = 0x80;

        public readonly struct Corner
        {
            public readonly int X, Y, Z;
            public readonly byte R, G, B;
            public Corner(int x, int y, int z, (byte R, byte G, byte B) c) { X = x; Y = y; Z = z; R = c.R; G = c.G; B = c.B; }
        }

        /// <summary>One gouraud quad, corners in the PSX's order (v0 v1 / v2 v3: triangles 0-1-2 and 1-2-3).
        /// <see cref="Culled"/>: drawn only when v0, v1, v2 turn clockwise on screen (a band side); a lid quad
        /// is drawn whichever way it faces.</summary>
        public readonly struct Quad
        {
            public readonly Corner V0, V1, V2, V3;
            public readonly bool Culled;
            public Quad(Corner v0, Corner v1, Corner v2, Corner v3, bool culled) { V0 = v0; V1 = v1; V2 = v2; V3 = v3; Culled = culled; }
        }

        /// <summary>The pulse, 0..256 world units, for the park timer (frame time units).</summary>
        public static int Pulse(long timer) => Math.Abs(EntranceFlags.Sin((int)((timer * 5 >> 9) & 0xFFF)) * 256 >> 12);

        /// <summary>The box, as 0x8001A70C builds it: footprint corner (x0, z0) and size w × d in world units, base
        /// height y0, the object's height in tiles, the footprint's centre (cx, cz), the fade (0..128) and the
        /// pulse.</summary>
        public static List<Quad> Build(int x0, int y0, int z0, int w, int d, int heightTiles, int cx, int cz, int fade, int pulse)
        {
            var glow = Scale(Glow, fade);
            var lid = Scale(Lid, fade);
            int hw = w >> 1, hd = d >> 1;
            var ring = Ring(x0, y0, z0, w, d, hw, hd, 0);
            var bright = glow;
            var dim = Shift(glow);
            int total = ((heightTiles << 8) + 0x100 + pulse) * 0x10;
            int step = total / 4;
            var quads = new List<Quad>();
            for (int angle16 = 0x2000, h = step; step > 0 && h <= total; angle16 += 0x2000, h += step)
            {
                int a = angle16 >> 4;
                int bulge = EntranceFlags.Sin(a & 0xFFF) * (200 - pulse) >> 12;
                var lower = ring;
                var lowerBright = bright;
                var lowerDim = dim;
                ring = Ring(x0, y0 + (h >> 4), z0, w, d, hw, hd, bulge);
                int f = a < 0x400 ? 0x400 - a : a - 0x400;
                bright = ((byte)(glow.R * f >> 10), (byte)(glow.G * f >> 10), (byte)(glow.B * f >> 10));
                dim = Shift(bright);
                for (int i = 0; i < 8; i++)
                {
                    int j = (i + 1) & 7;
                    bool even = (i & 1) == 0;
                    quads.Add(new Quad(
                        new Corner(ring[i].X, ring[i].Y, ring[i].Z, even ? bright : dim),
                        new Corner(ring[j].X, ring[j].Y, ring[j].Z, even ? dim : bright),
                        new Corner(lower[i].X, lower[i].Y, lower[i].Z, even ? lowerBright : lowerDim),
                        new Corner(lower[j].X, lower[j].Y, lower[j].Z, even ? lowerDim : lowerBright), true));
                }
            }
            // The lid: from each corner of the top ring to its two mid-edges and the centre, at the top ring's height.
            int top = ring[0].Y;
            for (int i = 0; i < 8; i += 2)
            {
                var p = ring[i]; var n = ring[(i + 1) & 7]; var b = ring[(i + 7) & 7];
                quads.Add(new Quad(new Corner(p.X, p.Y, p.Z, glow), new Corner(n.X, n.Y, n.Z, lid),
                                   new Corner(b.X, b.Y, b.Z, lid), new Corner(cx, top, cz, lid), false));
            }
            return quads;
        }

        /// <summary>Corners and mid-edges round the footprint, pushed out by <paramref name="bulge"/>: (x0, z0),
        /// along +x, down +z, back along −x and up −z.</summary>
        static (int X, int Y, int Z)[] Ring(int x0, int y, int z0, int w, int d, int hw, int hd, int bulge) => new[]
        {
            (x0 - bulge, y, z0 - bulge), (x0 + hw, y, z0 - bulge), (x0 + w + bulge, y, z0 - bulge), (x0 + w + bulge, y, z0 + hd),
            (x0 + w + bulge, y, z0 + d + bulge), (x0 + hw, y, z0 + d + bulge), (x0 - bulge, y, z0 + d + bulge), (x0 - bulge, y, z0 + hd),
        };

        static (byte R, byte G, byte B) Scale((byte R, byte G, byte B) c, int fade) =>
            ((byte)(c.R * fade >> 7), (byte)(c.G * fade >> 7), (byte)(c.B * fade >> 7));

        static (byte R, byte G, byte B) Shift((byte R, byte G, byte B) c) => ((byte)(c.R >> 3), (byte)(c.G >> 3), (byte)(c.B >> 3));

        /// <summary>The object's height for the box (slot 0x6C, 0x80062C6C): the model's extent in y from the ground
        /// up (a bottom below the ground counts as the ground), in whole tiles.</summary>
        public static int HeightTiles(int minY, int maxY) => Math.Abs(maxY - Math.Max(minY, 0)) >> 8;
    }

    /// <summary>The three highlight slots (0x80104AB4: object, target, fade) and how they fade.
    ///
    /// ⭐ FROM THE GAME'S CODE. Every park frame each slot in use eases its fade toward its target, by half the gap
    /// while rising and a quarter while falling, frees itself below 9, and has its target cleared (0x8001AF90 →
    /// 0x8001A6A8). The object under the cursor then asks for full strength (0x8001B064): its slot's target
    /// goes to 128, or it takes a free slot (target 128, fade 0), or, all three busy, a slot the game's own
    /// search picks (it compares targets against the running least FADE, a slip kept as the game has it). So
    /// a box swells in over a few frames, and one left behind fades out while the next swells in.</summary>
    public sealed class SelectionFades<T> where T : class
    {
        public const int Slots = 3;
        readonly T[] _object = new T[Slots];
        readonly int[] _target = new int[Slots], _fade = new int[Slots];

        /// <summary>The slots in use: their objects and fades.</summary>
        public IEnumerable<(T Object, int Fade)> Active
        {
            get { for (int i = 0; i < Slots; i++) if (_object[i] != null) yield return (_object[i], _fade[i]); }
        }

        /// <summary>One park frame's fade (0x8001A6A8 on every slot in use).</summary>
        public void Step()
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_object[i] == null) continue;
                int fade = _fade[i];
                fade -= (fade - _target[i]) >> (fade < _target[i] ? 1 : 2);
                _fade[i] = fade;
                if (fade < 9) Free(i);
                _target[i] = 0;
            }
        }

        /// <summary>The object under the cursor this frame (0x8001B064).</summary>
        public void Hover(T obj)
        {
            if (obj == null) return;
            for (int i = 0; i < Slots; i++)
                if (_object[i] == obj) { _target[i] = SelectionBox.FullFade; return; }
            int slot = Array.IndexOf(_object, null);
            if (slot < 0)
            {
                slot = 0;
                int least = 1000;
                for (int i = 0; i < Slots; i++)
                    if (_target[i] < least) { least = _fade[i]; slot = i; }
            }
            _object[slot] = obj; _target[slot] = SelectionBox.FullFade; _fade[slot] = 0;
        }

        public void Clear() { for (int i = 0; i < Slots; i++) Free(i); }

        void Free(int i) { _object[i] = null; _target[i] = 0; _fade[i] = 0; }
    }
}
