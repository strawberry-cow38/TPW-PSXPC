using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>One textured quad the game handed the GPU, in 320x256 screen space.</summary>
    public readonly struct ScreenQuad
    {
        public readonly short X0, Y0, X1, Y1, X2, Y2, X3, Y3;
        public readonly byte U0, V0, U1, V1, U2, V2, U3, V3;
        public readonly ushort Clut, TPage;
        /// <summary>Drawn additively (the highlight glow). Not a blend of the sprite over the ground.</summary>
        public readonly bool Additive;

        public ScreenQuad(short x0, short y0, short x1, short y1, short x2, short y2, short x3, short y3,
                          byte u0, byte v0, byte u1, byte v1, byte u2, byte v2, byte u3, byte v3,
                          ushort clut, ushort tpage, bool additive)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; X3 = x3; Y3 = y3;
            U0 = u0; V0 = v0; U1 = u1; V1 = v1; U2 = u2; V2 = v2; U3 = u3; V3 = v3;
            Clut = clut; TPage = tpage; Additive = additive;
        }
    }

    /// <summary>One untextured Gouraud triangle in screen space, colour per vertex.</summary>
    public readonly struct ScreenTri
    {
        public readonly short X0, Y0, X1, Y1, X2, Y2;
        public readonly byte R0, G0, B0, R1, G1, B1, R2, G2, B2;
        public ScreenTri(short x0, short y0, short x1, short y1, short x2, short y2,
                         byte r0, byte g0, byte b0, byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
        { X0=x0; Y0=y0; X1=x1; Y1=y1; X2=x2; Y2=y2;
          R0=r0; G0=g0; B0=b0; R1=r1; G1=g1; B1=b1; R2=r2; G2=g2; B2=b2; }
    }

    /// <summary>The main menu's backdrop, exactly as the console drew it, and the font's glyph table.
    /// Generated from findings/menu-art.json (twelfth report), which recorded the GPU's own display
    /// list; see findings/menu-art.md for how each piece was tied to its disc entry.
    ///
    /// ⚠ THIS IS A CAPTURED FRAME, NOT THE MENU'S LAYOUT LOGIC. These are the primitives the GPU was
    /// handed at ONE instant. The 28 kept here are the STATIC ones -- curtains, valance, floor, logo,
    /// highlight glow -- which is why replaying them is legitimate: they do not move. The 46 font
    /// primitives from the same frame are deliberately NOT here, because they spell out one particular
    /// menu with the highlight on one particular item; text is drawn through <see cref="Glyph"/>
    /// instead so the menu's real behaviour drives it.
    ///
    /// ⚠ The sweeping spotlight is NOT in here either, and not because it was missed: it is 35
    /// untextured additive Gouraud quads generated per frame, so a captured copy would be a spotlight
    /// frozen at one angle. Same for the language screen's background gradient and its arrows.</summary>
    public static class MenuLayout
    {
        /// <summary>The framebuffer these coordinates are in.
        ///
        /// ⚠ 512 WIDE, NOT 320. The legal screen and the FMVs run at 320x240, and assuming the menu
        /// did too clipped the THEME PARK WORLD logo in half -- its right-hand sprite is drawn out to
        /// x=424. The console switches to 512x240 before the language screen and stays there; the
        /// captured primitives span x 0..514 and y 5..256, which is the arithmetic that settles it
        /// rather than the mode line. Drawn at 512x256 so nothing authored below the displayed 240
        /// rows is thrown away before Godot scales it.</summary>
        public const int ScreenWidth = 512, ScreenHeight = 256;

        /// <summary>How many of those rows the console actually DISPLAYS: the top 240. The drawing area
        /// is 256 tall and the bottom 16 rows are never shown.
        ///
        /// ⭐ MEASURED, and the direction matters: the crop is at the BOTTOM, not split between top and
        /// bottom. Two independent landmarks on two different screens put the offset at zero -- the
        /// language ring's chevrons occupy display-list rows 192..216 and appear at displayed rows
        /// 192..214, and the menu's three text baselines at 160/192/224 land on the same rows of the
        /// captured frame. A centred crop would have shifted both by 8.
        ///
        /// ⚠ Drawing must still use the full 256, because the coordinates in the display list are
        /// drawing-area coordinates. Only the PRESENTATION is 240. Rendering 240 and clipping there
        /// would move nothing on screen but would silently drop anything the console draws below the
        /// fold and relies on being there when the display window moves.</summary>
        public const int VisibleHeight = 240;

        /// <summary>The FOLIO entry every one of these quads samples.</summary>
        public const int Sheet = 84;

        /// <summary>⚠ THE CAPTURED LIST IS PALINDROMIC -- it holds the scene TWICE, once forward
        /// and once reversed -- and these are the DEDUPLICATED quads. Replaying it whole drew
        /// every piece twice: the additive highlight glow came out at double brightness, and the
        /// floor, appearing in both halves, ended up painted over the curtains whichever way the
        /// list was walked. Both were reported as separate art faults; they were one duplicate.
        public static readonly ScreenQuad[] Backdrop =
        {
            new(87,66,253,66,87,136,253,136, 151,166,151,0,221,166,221,0, 16480, 25, false),
            new(253,64,424,64,253,137,424,137, 1,175,172,175,1,248,172,248, 16481, 25, false),
            new(0,213,128,213,0,256,128,256, 207,128,207,0,250,128,250,0, 16673, 26, false),
            new(128,213,256,213,128,256,256,256, 151,128,151,0,194,128,194,0, 16674, 27, false),
            new(256,213,384,213,256,256,384,256, 194,128,194,0,237,128,237,0, 16675, 27, false),
            new(384,213,512,213,384,256,512,256, 1,88,129,88,1,131,129,131, 16736, 28, false),
            new(174,130,338,130,174,174,338,174, 151,177,157,177,151,221,157,221, 16672, 56, true),
            new(105,130,174,130,105,174,174,174, 151,133,218,133,151,177,218,177, 16611, 56, true),
            new(338,130,407,130,338,174,407,174, 219,133,152,133,219,177,152,177, 16611, 56, true),
            new(10,5,132,5,10,129,132,129, 1,132,1,10,125,132,125,10, 16739, 24, false),
            new(132,5,267,5,132,58,267,58, 1,175,136,175,1,228,136,228, 16800, 27, false),
            new(10,129,101,129,10,245,101,245, 125,101,125,10,241,101,241,10, 16801, 24, false),
            new(392,5,514,5,392,129,514,129, 1,11,1,133,125,11,125,133, 16739, 24, false),
            new(270,5,405,5,270,58,405,58, 135,175,0,175,135,228,0,228, 16800, 27, false),
            new(423,129,514,129,423,245,514,245, 125,11,125,102,241,11,241,102, 16801, 24, false),
        };

        /// <summary>The sweeping light, as the console draws it: a fan of ADDITIVE Gouraud wedges
        /// from an apex off the top-left corner at (-103,-64). No artwork is involved -- searching the
        /// archive for a spotlight sprite would find nothing.
        ///
        /// ⚠ CAPTURED AT ONE INSTANT. On the console the arc end moves every frame, so this is the
        /// sweep frozen mid-stroke. Drawing it static is right for the shape and wrong for the motion,
        /// and the motion is the thing a player actually notices.
        ///
        /// ⚠ WHICH ALSO MEANS A SINGLE FRAME CANNOT BE COMPARED FOR BRIGHTNESS. Two captures of a
        /// moving sweep are at different phases, so whichever areas the rays currently cover read
        /// brighter. A port frame looking "too strong" beside a console frame is the expected result of
        /// comparing two instants, not evidence of a blend or depth error -- I drew exactly that wrong
        /// conclusion and moved the rays behind the curtains because of it.
        ///
        /// WHAT ANIMATING IT NEEDS, and what was already tried so nobody repeats it. 433 consecutive
        /// console frames of the menu were captured and measured two ways:
        ///   - the rays DO move: the brightness centroid over the stage travels about 12 px and
        ///     reverses direction, so the sweep is an oscillation, not a rotation.
        ///   - the PERIOD could not be extracted from pixels. Turning points came out 26 to 70 frames
        ///     apart, which is noise, and the whole-frame difference against a reference never returns
        ///     toward zero across 230 frames -- so the pattern does not repeat inside the capture.
        /// Pixel data is the wrong instrument here: the signal is a 12 px centroid shift buried in a
        /// static backdrop. The fan's arc endpoints are named explicitly in the GPU display list, so
        /// dumping THAT per frame gives the motion directly instead of inferring it.
        ///
        /// READING THE EXECUTABLE WAS TRIED NEXT AND THE ENTRY POINT IS NOT THERE. The fan's constants
        /// are not literal anywhere: the ray colour 0x1818 occurs ONCE in TPW.BIN, in data, referenced
        /// by no instruction, and not at all in any of the twelve decompressed overlays. The apex
        /// (-103,-64) "hits" were false positives -- `j 0x8007fe64` encodes as 0x0801FF99, whose low
        /// halfword is -103. Searching for a 16-bit value in MIPS code finds jump targets. So the
        /// geometry is computed, and locating the routine needs a write-watchpoint that reports the PC,
        /// which this harness does not have.
        ///
        /// ⚠ AND A RAM SWEEP NEARLY PRODUCED A FALSE ANSWER. Diffing console RAM across menu frames
        /// gives 43 words advancing at a constant rate; correlating them against the measured ray
        /// motion scored 0.80-0.84, which reads as an identification. A control of 200 sines with
        /// RANDOM period and phase scores a median of 0.788 and a maximum of 0.856 against the same
        /// signal. The candidates are inside the chance band and identify nothing -- they are all
        /// simply frame counters. A smooth 230-sample signal will correlate with almost any slow
        /// periodic function; without the control that number would have gone in as a finding.</summary>
        public static readonly ScreenTri[] Spotlight =
        {
            new(-103,-64,-103,-64,588,187, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,598,176,588,187, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,598,176,611,160, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,611,160, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,620,149,611,160, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,607,164, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,615,153,607,164, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,615,153,622,147, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,622,147, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,630,135,622,147, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,532,239, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,543,229,532,239, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,543,229,543,229, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,543,229, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,555,219,543,229, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,427,310, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,441,301,427,310, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,441,301,444,300, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,444,300, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,457,292,444,300, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,558,217, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,567,207,558,217, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,567,207,575,199, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,575,199, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,587,189,575,199, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,553,222, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,563,211,553,222, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,563,211,590,184, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,590,184, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,599,173,590,184, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,486,274, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,499,264,486,274, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,499,264,503,261, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,503,261, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,516,252,503,261, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,564,210, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,575,200,564,210, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,575,200,590,185, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,590,185, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,599,175,590,185, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,431,307, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,446,299,431,307, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,446,299,479,278, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,479,278, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,492,269,479,278, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,599,175, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,607,164,599,175, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,607,164,623,144, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,623,144, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,631,133,623,144, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,422,312, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,438,304,422,312, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,438,304,447,298, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,447,298, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,462,289,447,298, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,583,191, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,593,181,583,191, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,593,181,606,166, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,606,166, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,615,155,606,166, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,411,319, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,425,311,411,319, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,425,311,439,302, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,439,302, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,454,294,439,302, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,620,148, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,628,137,620,148, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,628,137,638,124, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,638,124, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,644,113,638,124, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,598,175, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,607,164,598,175, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,607,164,611,160, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,556,218, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,567,208,556,218, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,567,208,575,200, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,575,200, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,585,190,575,200, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,343,350, 0,0,0, 24,24,0, 0,0,0),
            new(-103,-64,359,343,343,350, 24,24,0, 24,24,0, 0,0,0),
            new(-103,-64,359,343,377,335, 24,24,0, 24,24,0, 24,24,0),
            new(-103,-64,-103,-64,377,335, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,393,328,377,335, 24,24,0, 0,0,0, 24,24,0),
            new(-103,-64,-103,-64,375,336, 0,0,0, 24,24,0, 24,24,0),
            new(-103,-64,391,328,375,336, 24,24,0, 0,0,0, 24,24,0),
        };

        /// <summary>Character to sprite index in sheet 84.
        ///
        /// ⚠ A-Z, a-z and 0-9 were derived from the strings the menu actually draws; the punctuation
        /// and accents were read BY EYE off a rendered sprite sheet, and the report says so. Treat a
        /// wrong punctuation glyph as expected rather than as a decode bug.</summary>
        public static readonly IReadOnlyDictionary<char, int> Glyph = new Dictionary<char, int>
        {
            ['A'] = 9,
            ['B'] = 10,
            ['C'] = 11,
            ['D'] = 12,
            ['E'] = 13,
            ['F'] = 14,
            ['G'] = 15,
            ['H'] = 16,
            ['I'] = 17,
            ['J'] = 18,
            ['K'] = 19,
            ['L'] = 20,
            ['M'] = 21,
            ['N'] = 22,
            ['O'] = 23,
            ['P'] = 24,
            ['Q'] = 25,
            ['R'] = 26,
            ['S'] = 27,
            ['T'] = 28,
            ['U'] = 29,
            ['V'] = 30,
            ['W'] = 31,
            ['X'] = 32,
            ['Y'] = 33,
            ['Z'] = 34,
            ['0'] = 35,
            ['1'] = 36,
            ['2'] = 37,
            ['3'] = 38,
            ['4'] = 39,
            ['5'] = 40,
            ['6'] = 41,
            ['7'] = 42,
            ['8'] = 43,
            ['9'] = 44,
            ['·'] = 45,
            ['!'] = 46,
            ['"'] = 47,
            ['%'] = 48,
            ['&'] = 49,
            ['*'] = 50,
            ['('] = 51,
            [')'] = 52,
            ['-'] = 53,
            [':'] = 54,
            [';'] = 55,
            [','] = 56,
            ['?'] = 57,
            ['\\'] = 58,
            ['/'] = 59,
            ['£'] = 60,
            ['$'] = 61,
            ['\''] = 62,
            ['['] = 63,
            [']'] = 64,
            ['a'] = 65,
            ['b'] = 66,
            ['c'] = 67,
            ['d'] = 68,
            ['e'] = 69,
            ['f'] = 70,
            ['g'] = 71,
            ['h'] = 72,
            ['i'] = 73,
            ['j'] = 74,
            ['k'] = 75,
            ['l'] = 76,
            ['m'] = 77,
            ['n'] = 78,
            ['o'] = 79,
            ['p'] = 80,
            ['q'] = 81,
            ['r'] = 82,
            ['s'] = 83,
            ['t'] = 84,
            ['u'] = 85,
            ['v'] = 86,
            ['w'] = 87,
            ['x'] = 88,
            ['y'] = 89,
            ['z'] = 90,
            ['ü'] = 91,
            ['å'] = 92,
            ['ö'] = 93,
            ['ú'] = 94,
            ['é'] = 95,
            ['á'] = 96,
            ['ä'] = 97,
            ['î'] = 98,
            ['ç'] = 99,
            ['ñ'] = 100,
        };
    }
}
