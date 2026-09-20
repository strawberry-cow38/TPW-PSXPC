using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The park HUD's font, as the game sets it up (0x8002AE18 → 0x8002AB78): glyphs are sprites of the
    /// common sheet (#416), looked up by character in the table at 0x800DD0E4 (one word per character from 0x20,
    /// the sprite index or −1). A glyph advances by its sprite's width plus <see cref="Spacing"/>; a character
    /// with no sprite (the space) by <see cref="SpaceAdvance"/> (0x8002A5E4); lines are <see cref="LineHeight"/>
    /// apart. A glyph's sprite is placed at the pen plus the sprite's own offsets (0x80029274), and the glyphs are
    /// stored turned in the sheet (their Flags byte), drawn upright (0x8002940C).
    ///
    /// Alignment is the font's +0x20 (0x80029D28): 0 from the left, 1 centred on x, 2 ending at x; the width a
    /// line is centred on is the sum of its advances.</summary>
    public sealed class HudFont
    {
        const uint CharMap = 0x800DD0E4;
        public const int FirstChar = 0x20, Chars = 224;
        public const int LineHeight = 14, SpaceAdvance = 7, Spacing = 1;
        readonly int[] _sprite = new int[Chars];

        public static HudFont Read(byte[] exe, uint baseAddress)
        {
            int o = (int)(CharMap - baseAddress);
            if (exe == null || o < 0 || o + Chars * 4 > exe.Length) return null;
            var f = new HudFont();
            for (int i = 0; i < Chars; i++) f._sprite[i] = BitConverter.ToInt32(exe, o + i * 4);
            return f;
        }

        /// <summary>The glyph sprite for a character, or −1 (the game draws nothing and advances a space).</summary>
        public int SpriteFor(char c) => c >= FirstChar && c < FirstChar + Chars ? _sprite[c - FirstChar] : -1;

        public int Advance(char c, TextureSheet sheet)
        {
            int s = SpriteFor(c);
            return s < 0 || sheet == null || s >= sheet.Sprites.Count ? SpaceAdvance : sheet.Sprites[s].W + Spacing;
        }

        public int Width(string text, TextureSheet sheet)
        {
            int w = 0;
            foreach (char c in text) w += Advance(c, sheet);
            return w;
        }

        /// <summary>A one-line string's glyphs: sprite and pen position (the sprite's offsets not yet added).</summary>
        public List<(int Sprite, int X, int Y)> Layout(string text, int x, int y, int align, TextureSheet sheet)
        {
            var glyphs = new List<(int, int, int)>();
            if (string.IsNullOrEmpty(text)) return glyphs;
            int pen = align switch { 1 => x - Width(text, sheet) / 2, 2 => x - Width(text, sheet), _ => x };
            foreach (char c in text)
            {
                int s = SpriteFor(c);
                if (s >= 0) glyphs.Add((s, pen, y));
                pen += Advance(c, sheet);
            }
            return glyphs;
        }
    }

    /// <summary>The HUD's numbers, as the game formats them (English).</summary>
    public static class HudText
    {
        /// <summary>0x800402C4: a minus for a debt, the currency sign first (it goes after the number in one
        /// language), then the digits with a comma between thousands (0x80040544).</summary>
        public static string Money(long pounds)
        {
            var s = new System.Text.StringBuilder();
            if (pounds < 0) { s.Append('-'); pounds = -pounds; }
            s.Append('$');
            string digits = pounds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int n = digits.Length, k = n < 4 ? 3 : n % 3;
            for (int i = 0; i < n; i++)
            {
                s.Append(digits[i]);
                if (--k < 0) k = 2;
                if (k == 0 && i != n - 1) s.Append(',');
            }
            return s.ToString();
        }

        /// <summary>0x800407A4: day, month (both 1-based, two digits) and year (four), with slashes; one language
        /// puts the month first.</summary>
        public static string Date(int day, int month, int year) => $"{day:00}/{month:00}/{year:0000}";
    }

    /// <summary>The four button prompts round the top right of the park HUD (0x80037CE8), and how they change.
    ///
    /// ⭐ FROM THE GAME'S CODE. Each button is a bubble sprite of the common sheet (triangle, circle, cross, square)
    /// at a fixed place, with its label (a string of the language table) centred 26 right of and 20 below it. The
    /// labels are set per tool from the table at 0x800EFC2C (four string ids per tool, 0x800193A4), and with no
    /// tool open, every frame from what the cursor is over (0x800396FC): Build / Laptop, or Info over something /
    /// Path, or OK over something / Hire. A label that changes FLIPS its bubble: eight frames of the bubble turning
    /// (0x80039B84 starts it), while the old label fades out and the new one in, both added onto the screen, over
    /// fourteen ticks.</summary>
    public sealed class HudPrompts
    {
        public const int Buttons = 4;
        /// <summary>Top (triangle), right (circle), bottom (cross), left (square): the bubbles' places (0x800E02E8).</summary>
        public static readonly (int X, int Y)[] Position = { (370, 16), (420, 32), (370, 48), (320, 32) };
        /// <summary>Where a label is centred, from its bubble's place.</summary>
        public const int LabelX = 26, LabelY = 20;
        /// <summary>The flip lasts until the counter passes this many ticks.</summary>
        public const int FlipTicks = 14;
        const uint FrameTables = 0x800E02F8;              // circle, cross, square, then triangle, 8 × s16 each
        static readonly int[] FrameTableOf = { 3, 0, 1, 2 }; // button → table (0x800E0328, 0x800E02F8, 0x800E0308, 0x800E0318)
        const uint ToolTable = 0x800EFC2C;

        public const int Blank = 0x124, Build = 0x1C2, Laptop = 0x22, Info = 0x126, Path = 0x1BE, Ok = 0x1CC, Hire = 0x111;

        readonly int[][] _frames = new int[Buttons][];
        readonly int[] _current = new int[Buttons], _previous = new int[Buttons];
        readonly double[] _counter = new double[Buttons];
        readonly bool[] _flipping = new bool[Buttons];
        readonly short[] _tools;

        HudPrompts(int[][] frames, short[] tools)
        {
            _frames = frames; _tools = tools;
            for (int i = 0; i < Buttons; i++) _current[i] = _previous[i] = Blank;
        }

        public static HudPrompts Read(byte[] exe, uint baseAddress)
        {
            int f = (int)(FrameTables - baseAddress), t = (int)(ToolTable - baseAddress);
            if (exe == null || f < 0 || f + 4 * 16 > exe.Length || t < 0 || t + 19 * 8 > exe.Length) return null;
            var frames = new int[Buttons][];
            for (int b = 0; b < Buttons; b++)
            {
                frames[b] = new int[8];
                for (int k = 0; k < 8; k++) frames[b][k] = BitConverter.ToInt16(exe, f + FrameTableOf[b] * 16 + k * 2);
            }
            var tools = new short[19 * 4];
            for (int i = 0; i < tools.Length; i++) tools[i] = BitConverter.ToInt16(exe, t + i * 2);
            return new HudPrompts(frames, tools);
        }

        /// <summary>The four string ids a tool shows (0x800EFC2C + tool × 8).</summary>
        public int[] ForTool(int tool)
        {
            if (tool < 0 || tool * 4 + 3 >= _tools.Length) return new[] { Blank, Blank, Blank, Blank };
            return new int[] { _tools[tool * 4], _tools[tool * 4 + 1], _tools[tool * 4 + 2], _tools[tool * 4 + 3] };
        }

        /// <summary>Set the four labels (0x80039678 → 0x80039B84): a label that differs starts its flip.</summary>
        public void Set(IReadOnlyList<int> ids)
        {
            for (int b = 0; b < Buttons && b < ids.Count; b++)
            {
                if (_current[b] == ids[b]) continue;
                _flipping[b] = true;
                _previous[b] = _current[b];
                _current[b] = ids[b];
            }
        }

        /// <summary>Advance the flips by <paramref name="ticks"/> game ticks (the counter gains the frame time
        /// over 4096 each frame, 0x80037CE8).</summary>
        public void Step(double ticks)
        {
            for (int b = 0; b < Buttons; b++)
            {
                if (!_flipping[b]) continue;
                _counter[b] += ticks;
                if (_counter[b] > FlipTicks) { _flipping[b] = false; _counter[b] = 0; }
            }
        }

        /// <summary>What to draw for a button: its bubble sprite this frame, its label, and during a flip the old
        /// label and the fade (0..128: the new label's colour; the old one's is 128 minus it).</summary>
        public (int Sprite, int Label, int Previous, int Fade, bool Flipping) State(int b)
        {
            int c = (int)_counter[b];
            int sprite = _frames[b][Math.Clamp(c >> 1, 0, 7)];
            int fade = Math.Clamp(((c + 1) >> 1) << 4, 0, 0x80);
            return (sprite, _current[b], _previous[b], fade, _flipping[b]);
        }
    }

    /// <summary>Where the park HUD puts things, in the PlayStation's 512 x 240 screen (0x80038B1C and the
    /// routines it calls), and the colours it uses.</summary>
    public static class ParkHudLayout
    {
        public const int ScreenWidth = 512, ScreenHeight = 240;
        /// <summary>The balance (0x800390C8): from the left at (50, 50), white, or red (0xB0, 0, 0) at nothing or
        /// in debt.</summary>
        public const int MoneyX = 50, MoneyY = 50;
        public static readonly (byte R, byte G, byte B) MoneyColour = (0x80, 0x80, 0x80), DebtColour = (0xB0, 0, 0);
        /// <summary>The date (0x80039008): centred on (136, 222).</summary>
        public const int DateX = 136, DateY = 222;
        /// <summary>The cost of what the open tool is about to do (0x8001B1DC, drawn from the park's own frame at
        /// 0x80057E04): the tool's pending total, from the left at (48, 64) — just under the balance — in white,
        /// as "%s $%ld" (the string at 0x801026BC) with string <see cref="CostLabel"/>, "Cost:". A plain %ld, so
        /// unlike the balance it carries no thousands comma, and nothing is drawn while the total is zero.</summary>
        public const int CostX = 48, CostY = 64, CostLabel = 35;
        /// <summary>The video camera (0x800391B4): sprite 406 at (50, 20) (0x80102A74).</summary>
        public const int CameraSprite = 406, CameraX = 50, CameraY = 20;
        /// <summary>The message bubble (0x8003A22C → 0x8003B06C) at (50, 190): the count centred 8 right and 26
        /// down; a disc of two mirrored halves of sprite 298 (drawn from x − 8, the right half at x − 8 + its
        /// width) in a gradient from orange to yellow (0x80102A9C / 0x80102AA4), ringed by two mirrored halves of
        /// sprite 302 (left from x − 11) SUBTRACTED at a quarter strength (colour 0x40, blend mode 2), all 2 below
        /// y.</summary>
        public const int BubbleX = 50, BubbleY = 190, BubbleSprite = 298, BubbleShadow = 302;
        public const int BubbleTextX = 8, BubbleTextY = 26;
        public static readonly (byte R, byte G, byte B) BubbleTop = (0xFE, 0x91, 0x14), BubbleBottom = (0xE7, 0xC3, 0x1A);
        public const byte BubbleShadowColour = 0x40;
    }
}
