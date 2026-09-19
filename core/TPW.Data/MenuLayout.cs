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

        /// <summary>The FOLIO entry every one of these quads samples.</summary>
        public const int Sheet = 84;

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
            new(338,130,407,130,338,174,407,174, 219,133,152,133,219,177,152,177, 16611, 56, true),
            new(105,130,174,130,105,174,174,174, 151,133,218,133,151,177,218,177, 16611, 56, true),
            new(174,130,338,130,174,174,338,174, 151,177,157,177,151,221,157,221, 16672, 56, true),
            new(423,129,514,129,423,245,514,245, 125,11,125,102,241,11,241,102, 16801, 24, false),
            new(270,5,405,5,270,58,405,58, 135,175,0,175,135,228,0,228, 16800, 27, false),
            new(392,5,514,5,392,129,514,129, 1,11,1,133,125,11,125,133, 16739, 24, false),
            new(10,129,101,129,10,245,101,245, 125,101,125,10,241,101,241,10, 16801, 24, false),
            new(132,5,267,5,132,58,267,58, 1,175,136,175,1,228,136,228, 16800, 27, false),
            new(10,5,132,5,10,129,132,129, 1,132,1,10,125,132,125,10, 16739, 24, false),
            new(384,213,512,213,384,256,512,256, 1,88,129,88,1,131,129,131, 16736, 28, false),
            new(256,213,384,213,256,256,384,256, 194,128,194,0,237,128,237,0, 16675, 27, false),
            new(128,213,256,213,128,256,256,256, 151,128,151,0,194,128,194,0, 16674, 27, false),
            new(0,213,128,213,0,256,128,256, 207,128,207,0,250,128,250,0, 16673, 26, false),
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
