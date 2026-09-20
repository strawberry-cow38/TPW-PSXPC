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
            _top.Paint = on => { PaintCount(on); PaintContext(on); };
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

        /// <summary>Sprite s with its pen at PSX (px, py), measured from the left or the right; mirrored left-right
        /// when asked, with a colour for each corner (top-left, top-right, bottom-left, bottom-right), the GPU's
        /// gouraud, 128 = the texel as it is.</summary>
        void Sprite(CanvasItem on, int s, float px, float py, bool right, Color c0, Color c1, Color c2, Color c3, bool mirror = false)
        {
            var tex = Tex(s);
            if (tex == null) return;
            var sp = _sheet.Sprites[s];
            float x = (right ? Right(px) : Left(px)) + sp.OffsetX * Sx, y = (py + sp.OffsetY) * Sy;
            float w = sp.W * Sx, h = sp.H * Sy;
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
            var top = Psx((0xE7, 0x80, 0x1A));
            var bottom = Psx((0xE8, 0xCA, 0x2D));
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
