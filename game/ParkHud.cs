using System;
using System.Collections.Generic;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>The park HUD (0x80038B1C): the balance and the video camera top left, the four button prompts top
    /// right, the message bubble and the date bottom left, each drawn from the common sheet (#416) and in the
    /// game's font (<see cref="HudFont"/>) where <see cref="ParkHudLayout"/> puts it.
    ///
    /// ⭐ SCALED, NOT STRETCHED (master's call). The game lays the HUD out on a 512 x 240 screen shown 4:3, so a PSX
    /// pixel is a 240th of the window's height tall and a 384th of it wide, whatever the window's shape. Each piece
    /// keeps its distance from the side of the screen it sits against, measured in those pixels: the prompts from
    /// the right, everything else from the left. On a wide window the extra width opens up between them.</summary>
    public partial class ParkHud : Control
    {
        TextureSheet _sheet;
        HudFont _font;
        HudPrompts _prompts;
        StringTable _strings;
        readonly Dictionary<int, ImageTexture> _tex = new();
        Layer _sub, _add, _top;

        /// <summary>What the HUD shows, set by its owner every frame.</summary>
        public long Pounds;
        public int Day = 1, Month = 1, Year = 2000;
        public int Messages;
        /// <summary>What the open tool is about to charge, in pounds; zero draws nothing.</summary>
        public int Cost;

        static readonly double TicksPerSecond = EntranceFlags.TimeUnitsPerSecond / 4096.0;

        /// <summary>A layer drawn after the HUD with its own blend: the ring round the message bubble is subtracted
        /// (blend mode 2), a label changing is added (mode 1), the bubble's count sits on top.</summary>
        sealed partial class Layer : Control
        {
            public Action<Layer> Paint;
            public override void _Draw() => Paint?.Invoke(this);
        }

        public override void _Ready()
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            Layer Make(CanvasItemMaterial.BlendModeEnum blend)
            {
                var l = new Layer { MouseFilter = MouseFilterEnum.Ignore, Material = new CanvasItemMaterial { BlendMode = blend } };
                l.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(l);
                return l;
            }
            _sub = Make(CanvasItemMaterial.BlendModeEnum.Sub);
            _add = Make(CanvasItemMaterial.BlendModeEnum.Add);
            _top = Make(CanvasItemMaterial.BlendModeEnum.Mix);
            _sub.Paint = PaintRing;
            _add.Paint = PaintFlips;
            _top.Paint = on => { PaintCount(on); PaintPanel(on); PaintContext(on); };
        }

        public void Setup(TextureSheet common, byte[] exe, StringTable strings)
        {
            _sheet = common;
            _strings = strings;
            _font = HudFont.Read(exe, AssetSelfTest.GameExecutableBase);
            _prompts = HudPrompts.Read(exe, AssetSelfTest.GameExecutableBase);
            _tex.Clear();
            QueueRedraw();
        }

        public bool CanDraw => _sheet != null && _font != null && _prompts != null;

        /// <summary>The labels for a tool (the game's tool number, 0x800193A4).</summary>
        public int[] ToolPrompts(int tool) => _prompts?.ForTool(tool);

        public void SetPrompts(IReadOnlyList<int> ids) => _prompts?.Set(ids);

        public override void _Process(double delta)
        {
            if (!Visible || !CanDraw) return;
            _prompts.Step(delta * TicksPerSecond);
            QueueRedraw(); _sub.QueueRedraw(); _add.QueueRedraw(); _top.QueueRedraw();
        }

        // The PSX screen to this one: a PSX pixel is H / 240 tall and H / 384 wide (512 across a 4:3 view), H the
        // window's height.
        Vector2 Screen => GetViewportRect().Size;
        float Sy => Screen.Y / ParkHudLayout.ScreenHeight;
        float Sx => Screen.Y / 384f;
        float Left(float psxX) => psxX * Sx;

        /// <summary>⭐ THE HUD SPREADS, THE PANEL FILLS THE SCREEN. Every other coordinate in this file
        /// belongs to the park HUD, which anchors to the screen's edges and leaves the middle to the park.
        /// The attraction panel is not a HUD: it is a modal, and it is meant to BE the screen. On the PSX it
        /// covers 440x191 of a 512x240 screen and the park shows around the edge; here its own rect is mapped
        /// onto the whole window, so the backdrop reaches all four edges and every child keeps its place
        /// WITHIN the panel — the name stays centred in the info frame, the sliders stay centred in the
        /// control frame, because they are all carried by the same transform.
        ///
        /// ⚠ The info frame starts at x=16, 19px LEFT of the panel's own 35, so it runs a little past the
        /// left edge and is clipped. That is the overhang recorded in findings/panel.md, not a mistake here;
        /// it costs nothing while the frames are plain fills, and it is why the border sprites will have to
        /// wait for that question to be settled.</summary>
        const int PanelRectX = 35, PanelRectY = 31, PanelRectW = 440, PanelRectH = 191;

        /// <summary>The panel's transform while it paints; 0 scale = not painting it, so everything else —
        /// including the context menu, which is pinned to the cursor — keeps the HUD's Sx/Sy untouched.</summary>
        float _pSx, _pSy;
        float PX(float psx) => _pSx > 0f ? (psx - PanelRectX) * _pSx : psx * Sx;
        float PY(float psy) => _pSy > 0f ? (psy - PanelRectY) * _pSy : psy * Sy;
        float WX(float w) => (_pSx > 0f ? _pSx : Sx) * w;
        float WY(float h) => (_pSy > 0f ? _pSy : Sy) * h;
        float Right(float psxX) => Screen.X - (ParkHudLayout.ScreenWidth - psxX) * Sx;

        /// <summary>A sprite of the common sheet, upright, every texel that shows fully there (the HUD's opaque
        /// primitives draw bit-15 texels solid; the blended ones get their blend from the layer).</summary>
        ImageTexture Tex(int sprite)
        {
            if (_tex.TryGetValue(sprite, out var t)) return t;
            t = null;
            var img = _sheet?.RenderSprite(sprite);
            if (img != null)
            {
                var sp = _sheet.Sprites[sprite];
                int w = sp.W, h = sp.H;
                var rgba = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // A glyph is stored turned (0x8002940C): screen (x, y) is stored texel (y, w - 1 - x).
                        int sx = sp.Flags != 0 ? y : x, sy = sp.Flags != 0 ? w - 1 - x : y;
                        if (sx >= img.Width || sy >= img.Height) continue;
                        int si = (sy * img.Width + sx) * 4, di = (y * w + x) * 4;
                        if (img.Rgba[si + 3] == 0) continue;
                        rgba[di] = img.Rgba[si]; rgba[di + 1] = img.Rgba[si + 1]; rgba[di + 2] = img.Rgba[si + 2]; rgba[di + 3] = 255;
                    }
                if (w > 0 && h > 0) t = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba));
            }
            _tex[sprite] = t;
            return t;
        }

        static Color Psx((byte R, byte G, byte B) c) => new(c.R / 128f, c.G / 128f, c.B / 128f);

        /// <summary>⚠ A FLAT COLOUR IS NOT A TEXEL TINT. <see cref="Psx"/> divides by 128 because 128 is the
        /// GPU's "draw the texel as it is" — right for tinting a sprite, wrong for a quad with no texture,
        /// where the value IS the colour and anything over 128 blows out. The panel's orange came out the
        /// same yellow as its yellow until this existed.</summary>
        static Color Rgb((byte R, byte G, byte B) c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

        /// <summary>Sprite s with its pen at PSX (px, py), measured from the left or the right; mirrored left-right
        /// when asked, with a colour for each corner (top-left, top-right, bottom-left, bottom-right), the GPU's
        /// gouraud, 128 = the texel as it is.</summary>
        void Sprite(CanvasItem on, int s, float px, float py, bool right, Color c0, Color c1, Color c2, Color c3, bool mirror = false)
        {
            var tex = Tex(s);
            if (tex == null) return;
            var sp = _sheet.Sprites[s];
            float x = right ? Right(px) + sp.OffsetX * Sx : PX(px) + WX(sp.OffsetX);
            float y = PY(py + sp.OffsetY);
            float w = right ? sp.W * Sx : WX(sp.W), h = WY(sp.H);
            var pts = new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };
            var uv = mirror ? new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) }
                            : new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            on.DrawPrimitive(pts, new[] { c0, c1, c3, c2 }, uv, tex);
        }

        void Sprite(CanvasItem on, int s, float px, float py, bool right, Color c) => Sprite(on, s, px, py, right, c, c, c, c);

        void Text(CanvasItem on, string text, int px, int py, int align, bool right, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var (s, gx, gy) in _font.Layout(text, px, py, align, _sheet)) Sprite(on, s, gx, gy, right, c);
        }

        string Label(int id) => _strings?[id] ?? "";

        /// <summary>An attraction's panel as the HUD needs it: the numbers, already decided, with the LAYOUT
        /// left to the HUD because that is where every other coordinate in this port lives. Null = closed.
        ///
        /// A value of −1 on a bar means the port cannot supply it yet; the bar is drawn empty with its label
        /// dimmed rather than filled with an invention.</summary>
        public sealed class AttractionPanelView
        {
            public string Name = "";
            public string Age, Users;                      // null = not wired
            public int Excitement = -1, Reliability = -1, Repair = -1, Life = -1;
            public int Speed, SpeedMin, SpeedMax;
            public int Capacity, CapacityMin, CapacityMax; public bool ShowCapacity;
            public int Duration, DurationMin, DurationMax; public bool ShowDuration;
        }
        public AttractionPanelView Panel;

        /// <summary>The attraction panel's Details page, at the game's own coordinates (findings/panel.md §2).
        /// Six readings — Age and Users as text, then Excitement, Reliability, Repair and Life as 0..100 bars —
        /// and up to three sliders on the right.
        ///
        /// ⚠ RELIABILITY AND REPAIR ARE DIFFERENT NUMBERS: the projected value against the live one. The panel
        /// shows both, side by side, and that is the point of the page.
        ///
        /// ⚠ THE FRAMES AND BARS ARE FILLS ONLY. The game draws each frame's gradient UNDER sprite corners
        /// (0x169 + 0x148) and stretched edges (0x173, 0x161), and a bar under caps and a knob (0x163..0x170).
        /// Every rectangle, colour and coordinate here is the game's; the ornament is not drawn yet.</summary>
        void PaintPanel(CanvasItem on)
        {
            if (Panel == null) return;
            _pSx = Screen.X / PanelRectW; _pSy = Screen.Y / PanelRectH;
            try { PaintPanelAt(on); } finally { _pSx = _pSy = 0f; }
        }

        void PaintPanelAt(CanvasItem on)
        {
            var dim = Psx((0x40, 0x40, 0x40));
            var lit = Psx((0x80, 0x80, 0x80));
            // GUESS: the panel's own rect is READ (0x800446C4 writes 35/31/440/191 to the same +8/+10/+20/+22
            // the frames use), but nothing proves it is DRAWN, and its colour is unknown.  The four greys at
            // 0x80102B20 sit immediately before the two frame rects in the same data block and read like a
            // gouraud quad's corners, so they are the best candidate.  ⚠ The info frame starts 19px LEFT of
            // this rect, which is either how the game looks or a sign this rect is not the backdrop at all.
            Quad(on, 35, 31, 440, 191, Rgb((0x20, 0x20, 0x20)), Rgb((0x80, 0x80, 0x80)));   // backdrop
            Frame(on, 16, 64, 280, 152);                                                     // info frame
            Frame(on, 280, 80, 180, 110);                                                    // control frame
            Text(on, Panel.Name, 156, 80, 1, false, lit);

            Text(on, Label(0x1DC), 66, 114, 0, false, dim);
            Text(on, Panel.Age ?? "-", 126, 114, 1, false, Panel.Age == null ? dim : lit);
            Text(on, Label(0x18C), 206, 114, 0, false, dim);
            Text(on, Panel.Users ?? "-", 256, 114, 1, false, Panel.Users == null ? dim : lit);

            Reading(on, 0x37,  66, 144, 176, 134, Panel.Excitement);
            Reading(on, 0x3ED, 66, 164, 176, 154, Panel.Reliability);
            Reading(on, 0x25C, 66, 184, 176, 174, Panel.Repair);
            Reading(on, 0x3FA, 66, 204, 176, 194, Panel.Life);

            Slider(on, 0x1A1, 100, Panel.Speed, Panel.SpeedMin, Panel.SpeedMax);
            int y = 134;
            if (Panel.ShowCapacity) { Slider(on, 0x364, y, Panel.Capacity, Panel.CapacityMin, Panel.CapacityMax); y = 168; }
            if (Panel.ShowDuration) Slider(on, 0x2D5, y, Panel.Duration, Panel.DurationMin, Panel.DurationMax);

            // The page-name strip, bottom right, with its icon (a ride's is 0x141).
            Text(on, Label(0x39A), 354, 216, 0, false, lit);
        }

        /// <summary>A labelled 0..100 bar: the label at the left, the bar at (bx, by) 80 wide.</summary>
        void Reading(CanvasItem on, int label, int lx, int ly, int bx, int by, int value)
        {
            Text(on, Label(label), lx, ly, 0, false, Psx((0x40, 0x40, 0x40)));
            Bar(on, bx, by, 80, value, 0, 100);
        }

        /// <summary>A slider: its label centred above the bar, both at the control frame's x (findings §2).</summary>
        void Slider(CanvasItem on, int label, int y, int value, int min, int max)
        {
            Text(on, Label(label), 370, y - 4, 1, false, Psx((0x40, 0x40, 0x40)));
            Bar(on, 320, y, 100, value, min, max);
        }

        /// <summary>The game's bar: a teal track with a red fill, 0..100 of its range. ⚠ Caps and knob absent.</summary>
        void Bar(CanvasItem on, int x, int y, int w, int value, int min, int max)
        {
            var track = Rgb((0x00, 0x68, 0x72));
            Quad(on, x, y, w, 6, track, track);
            if (value < 0 || max <= min) return;
            int fill = Math.Clamp((value - min) * w / (max - min), 0, w);
            if (fill <= 0) return;
            var red = Rgb((0xF0, 0x40, 0x40));
            Quad(on, x + 1, y + 1, fill, 4, red, red);
        }

        /// <summary>A frame's fill: the gouraud quad the game puts under its border, orange to yellow.</summary>
        void Frame(CanvasItem on, int x, int y, int w, int h)
            => Quad(on, x, y, w, h, Rgb((0xE7, 0x80, 0x1A)), Rgb((0xE8, 0xCA, 0x2D)));

        /// <summary>A rectangle in PSX coordinates, shaded top colour to bottom colour.</summary>
        void Quad(CanvasItem on, int px, int py, int pw, int ph, Color top, Color bottom)
        {
            float x = PX(px), y = PY(py), w = WX(pw), h = WY(ph);
            on.DrawPrimitive(new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) },
                             new[] { top, top, bottom, bottom }, new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero });
        }

        /// <summary>The context list the right button opens on an attraction: the command labels, already
        /// resolved to words, and where the cursor was. Empty means nothing is open.
        ///
        /// ⭐ IT IS THE SAME LIST AS THE PANEL'S OPTIONS PAGE — the game fills both from 0x8004A0B4 — so
        /// whatever appears here will appear there when the panel is drawn.</summary>
        public System.Collections.Generic.List<string> ContextRows = new();
        public Vector2 ContextAt;
        /// <summary>Which row the pointer is over, or -1. The game highlights a row and takes CROSS on it.</summary>
        public int ContextPick = -1;

        /// <summary>The list, drawn as the game draws one (findings/panel.md §1): a framed box with its rows
        /// centred and the block centred vertically, pitch = line height + 2, the picked row bright and the
        /// rest dim, and "Not Available" when it is empty.
        ///
        /// ⚠ THE FRAME IS THE FILL ONLY SO FAR. The game's frame is a gouraud quad UNDER sprite corners
        /// (0x169 + 0x148) and stretched edges (0x173, 0x161) — the gradient and the geometry here are the
        /// game's, the ornamental border is not drawn yet. It is the next thing, not a design choice.</summary>
        void PaintContext(CanvasItem on)
        {
            if (ContextRows == null || ContextRows.Count == 0) return;
            const int rowPitch = 14, padY = 6, width = 180;
            int rows = Math.Max(1, ContextRows.Count);
            int h = rows * rowPitch + padY * 2;
            // ContextAt arrives in SCREEN pixels (the caller has a mouse, not a PSX pen), so it comes back
            // into PSX space here, where every other coordinate in this file already lives.
            int px = (int)(ContextAt.X / Sx), py = (int)(ContextAt.Y / Sy);
            float x = Left(px), y = py * Sy, w = width * Sx, hh = h * Sy;
            // The frame's own gradient: orange at the top, yellow at the bottom (DAT_80102AEC / DAT_80102AE4).
            var top = Rgb((0xE7, 0x80, 0x1A));
            var bottom = Rgb((0xE8, 0xCA, 0x2D));
            on.DrawPrimitive(new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + hh), new Vector2(x, y + hh) },
                             new[] { top, top, bottom, bottom }, new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero });
            for (int i = 0; i < ContextRows.Count; i++)
            {
                var c = Psx(i == ContextPick ? ((byte)0x80, (byte)0x80, (byte)0x80) : ((byte)0x40, (byte)0x40, (byte)0x40));
                Text(on, ContextRows[i], px + width / 2, py + padY + i * rowPitch + rowPitch - 2, 1, false, c);
            }
        }

        public override void _Draw()
        {
            if (!CanDraw) return;
            var white = Psx((0x80, 0x80, 0x80));
            // The video camera, then the balance: red once there is nothing left (0x800390C8).
            Sprite(this, ParkHudLayout.CameraSprite, ParkHudLayout.CameraX, ParkHudLayout.CameraY, false, white);
            Text(this, HudText.Money(Pounds), ParkHudLayout.MoneyX, ParkHudLayout.MoneyY, 0, false,
                 Psx(Pounds < 1 ? ParkHudLayout.DebtColour : ParkHudLayout.MoneyColour));
            // What the open tool is about to cost, under the balance (0x8001B1DC).
            if (Cost > 0)
                Text(this, $"{Label(ParkHudLayout.CostLabel)} ${Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                     ParkHudLayout.CostX, ParkHudLayout.CostY, 0, false, white);
            // The four prompts, each bubble and, when it is not flipping, its label.
            for (int b = 0; b < HudPrompts.Buttons; b++)
            {
                var (sprite, label, _, _, flipping) = _prompts.State(b);
                var (bx, by) = HudPrompts.Position[b];
                Sprite(this, sprite, bx, by, true, white);
                if (!flipping) Text(this, Label(label), bx + HudPrompts.LabelX, by + HudPrompts.LabelY, 1, true, white);
            }
            // The message bubble's disc: two mirrored halves in the orange-to-yellow gradient.
            var top = Psx(ParkHudLayout.BubbleTop); var bottom = Psx(ParkHudLayout.BubbleBottom);
            int x = ParkHudLayout.BubbleX, y = ParkHudLayout.BubbleY + 2;
            int dw = _sheet.Sprites[ParkHudLayout.BubbleSprite].W;
            Sprite(this, ParkHudLayout.BubbleSprite, x - 8 + dw, y, false, top, top, bottom, bottom);
            Sprite(this, ParkHudLayout.BubbleSprite, x - 8, y, false, top, top, bottom, bottom, mirror: true);
            // The date, centred.
            Text(this, HudText.Date(Day, Month, Year), ParkHudLayout.DateX, ParkHudLayout.DateY, 1, false, white);
        }

        /// <summary>The bubble's ring, subtracted at a quarter strength (0x8003B06C).</summary>
        void PaintRing(Layer on)
        {
            if (!CanDraw) return;
            var c = Psx((ParkHudLayout.BubbleShadowColour, ParkHudLayout.BubbleShadowColour, ParkHudLayout.BubbleShadowColour));
            int x = ParkHudLayout.BubbleX, y = ParkHudLayout.BubbleY + 2;
            int dw = _sheet.Sprites[ParkHudLayout.BubbleSprite].W;
            Sprite(on, ParkHudLayout.BubbleShadow, x - 8 + dw, y, false, c);
            Sprite(on, ParkHudLayout.BubbleShadow, x - 11, y, false, c, c, c, c, mirror: true);
        }

        /// <summary>A changing label: the old one fading out and the new one in, both added (0x80037CE8).</summary>
        void PaintFlips(Layer on)
        {
            if (!CanDraw) return;
            for (int b = 0; b < HudPrompts.Buttons; b++)
            {
                var (_, label, previous, fade, flipping) = _prompts.State(b);
                if (!flipping) continue;
                var (bx, by) = HudPrompts.Position[b];
                byte old = (byte)(0x80 - fade), now = (byte)fade;
                Text(on, Label(previous), bx + HudPrompts.LabelX, by + HudPrompts.LabelY, 1, true, Psx((old, old, old)));
                Text(on, Label(label), bx + HudPrompts.LabelX, by + HudPrompts.LabelY, 1, true, Psx((now, now, now)));
            }
        }

        /// <summary>The number of messages, centred on the bubble.</summary>
        void PaintCount(Layer on)
        {
            if (!CanDraw) return;
            Text(on, Messages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                 ParkHudLayout.BubbleX + ParkHudLayout.BubbleTextX, ParkHudLayout.BubbleY + ParkHudLayout.BubbleTextY, 1, false,
                 Psx((0x80, 0x80, 0x80)));
        }
    }
}
