using System;
using Godot;
using TPW.Data;

namespace TPWGodot
{
    /// <summary>The people, drawn: the game's own sprites in place of a coloured box.
    ///
    /// ⭐ ONE TEXTURE FOR EVERYBODY. <see cref="PeopleSheet"/> says which sprites belong to which person
    /// and where the walk sits inside each block; the whole sheet is rendered once, every sprite in its
    /// own palette, and each guest is a quad that reads its rectangle out of that one texture. So a park
    /// full of guests costs one texture and one material per guest, and changing frame is two numbers.
    ///
    /// ⭐ THE EIGHT FACINGS ARE THE VIEWER'S, NOT THE WORLD'S. A sprite set like this is drawn for one
    /// camera: row 0 is the figure walking AWAY from whoever is looking. The console's camera turns in
    /// quarters, so the game can pick a row from the world direction alone; the port's camera is free,
    /// so the row is picked from the direction the guest is walking RELATIVE TO THE CAMERA, which is the
    /// same thing wherever the two agree and stays right where they do not.
    ///
    /// ⭐ GUESTS ONLY. Which person a guest is drawn as is the game's (PeopleSheet.GuestBlocks); the staff
    /// and the world's costumed character are in the same sheet and are not guests.
    ///
    /// ⚠ THE SIZE IS CHECKED, NOT READ. The game's sprite call takes a divisor per axis and scales
    /// ((size − 1) &lt;&lt; 8) / divisor (0x80055470), so what a person measures on screen is the caller's to
    /// choose, and this has not found that call site. What it has is a frame off the real disc (tinyclaw,
    /// hardware): a guard standing at a Crazy Ape, 20 x 16 px on a 512 x 240 picture. The ride is four
    /// tiles across and spans about 120 px there, so a tile is ~30 px at that depth and the guard is
    /// ~0.53 of a tile tall. The guard's own sprites are 25-28 texels tall against a guest's 19, so a
    /// guest comes out at ~0.39 of a tile. <see cref="TilesPerTexel"/> draws one at 0.43 — inside the
    /// error of a measurement whose figure may be partly hidden by the ride's platform, so it stands.
    ///
    /// ⚠ What would settle it properly: a frame with a GUEST on a path, and a clean pixels-per-tile off
    /// something with a known footprint in the same shot.</summary>
    public sealed class GuestSprites
    {
        /// <summary>World size of one sprite texel, in tiles. ⚠ Chosen, not measured — see the class note.</summary>
        public const float TilesPerTexel = 1f / 44f;

        /// <summary>Print what a guest is actually being drawn as, once in a while. For captures.</summary>
        public static bool Debug;
        int _shouted;

        readonly PeopleSheet _people;
        readonly ImageTexture _atlas;
        readonly int _atlasW, _atlasH;

        /// <summary>The common sheet (FOLIO 416) baked for 3D, and the sheet itself for its sprite rects.
        /// ⚠ A SECOND ATLAS, because riders are not drawn from the people sheet at all: a rider is a HEAD
        /// out of 416, the same sheet the HUD and the particles use. findings/rider-positions.md §8.</summary>
        ImageTexture _heads;
        TextureSheet _headSheet;
        int _headsW, _headsH;

        /// <summary>Bake the common sheet too, once, so riders can be drawn. Safe to call repeatedly.</summary>
        public void SetCommonSheet(TextureSheet common)
        {
            if (_heads != null || common == null) return;
            var img = common.RenderSprites("common");
            if (img == null || img.Width <= 0 || img.Height <= 0) return;
            var rgba = (byte[])img.Rgba.Clone();
            for (int i = 0; i < rgba.Length; i += 4)
                if (rgba[i] == 24 && rgba[i + 1] == 16 && rgba[i + 2] == 28) rgba[i + 3] = 0;
            _heads = ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, rgba));
            _headSheet = common;
            _headsW = img.Width; _headsH = img.Height;
            // ⚠ THE OVERRIDE'S INDICES ARE THE GAME'S, AND THIS LIST IS THE PORT'S. findings say the six
            // developer faces measure 26..32 x 38..46 texels; if what lands at 564..569 here is not that
            // shape, the two lists do not line up and the override is drawing whatever else is there.
            if (TPW.Sim.RiderSprites.DeveloperHeadsEnabled)
                foreach (int i in TPW.Sim.RiderSprites.DeveloperHeads)
                    GD.Print(i < common.Sprites.Count
                        ? $"[tpw] devhead sprite {i}: {common.Sprites[i].W}x{common.Sprites[i].H}"
                          + $" at page ({common.Sprites[i].PageX},{common.Sprites[i].PageY})"
                          + $" uv ({common.Sprites[i].U},{common.Sprites[i].V})"
                        : $"[tpw] devhead sprite {i}: OUT OF RANGE ({common.Sprites.Count} sprites)");
        }

        public bool HasHeads => _heads != null;

        /// <summary>The sprite a stored facing and frame resolve to, for diagnostics.</summary>
        public int SpriteOf(int block, int stored, int frame) => _people?.WalkSprite(block, stored, frame) ?? -1;

        /// <summary>Draw a rider: sprite <paramref name="sprite"/> of the common sheet, centred on the seat
        /// rather than standing on it, mirrored in x when the game mirrors it, and rolled in the screen
        /// plane the way the original rolls the quad.</summary>
        public void DrawHead(MeshInstance3D inst, int sprite, bool mirror, float roll, Vector3 centre, Vector3 cameraForward)
        {
            if (_heads == null || _headSheet == null || sprite < 0 || sprite >= _headSheet.Sprites.Count) return;
            var sp = _headSheet.Sprites[sprite];
            float w = sp.W * TilesPerTexel, h = sp.H * TilesPerTexel;
            if (inst.Mesh is QuadMesh q && (q.Size.X != w || q.Size.Y != h)) q.Size = new Vector2(w, h);
            if (inst.MaterialOverride is StandardMaterial3D m)
            {
                int px = (sp.PageX - _headSheet.VramX) / 64 * TextureSheet.PageTexels + sp.U;
                int py = (sp.PageY - _headSheet.VramY) / TextureSheet.PageTexels * TextureSheet.PageTexels + sp.V;
                m.AlbedoTexture = _heads;
                // ⚠⚠ MIRROR THE QUAD, NOT THE UVs. Flipping by negating Uv1Scale.X puts the sprite's RIGHT
                // edge at uv.x = 0, which samples at px + W -- one texel PAST the sprite, into whatever sits
                // next to it in the atlas. In this sheet what sits next to a head is the HUD FONT: the
                // heads and the glyphs are interleaved in the same block, with "dF" one row above them.
                // A one-texel white sliver of a letter along the edge of a mirrored head is exactly what
                // master photographed. Keeping the UVs forward and negating the quad's right vector gives
                // the same mirrored picture and can never sample outside the rect.
                Inset(m, px, py, sp.W, sp.H, _headsW, _headsH);
            }
            var towards = -cameraForward;
            if (towards.LengthSquared() < 1e-6f) towards = Vector3.Back;
            towards = towards.Normalized();
            var right = Vector3.Up.Cross(towards);
            right = right.LengthSquared() < 1e-6f ? Vector3.Right : right.Normalized();
            var up = towards.Cross(right).Normalized();
            if (mirror) right = -right;
            if (roll != 0f)
            {
                float c = Mathf.Cos(roll), sn = Mathf.Sin(roll);
                var r2 = right * c + up * sn;
                up = up * c - right * sn;
                right = r2;
            }
            inst.Transform = new Transform3D(new Basis(right, up, towards), centre);
        }

        /// <summary>Put a guest's material back on the people atlas after it has been a rider.</summary>
        public void RestoreWalkTexture(MeshInstance3D inst)
        {
            if (inst?.MaterialOverride is StandardMaterial3D m && m.AlbedoTexture != _atlas) m.AlbedoTexture = _atlas;
        }

        GuestSprites(PeopleSheet people, ImageTexture atlas, int w, int h)
        { _people = people; _atlas = atlas; _atlasW = w; _atlasH = h; }

        public int Blocks => _people?.Count ?? 0;

        /// <summary>Bake the people sheet into one texture, or null when it is not there. The sheet is the
        /// one the host already parsed (entry <see cref="PeopleSheet.Sheet"/>).</summary>
        public static GuestSprites From(System.Collections.Generic.List<(GazEntry Entry, TextureSheet Sheet)> sheets)
        {
            TextureSheet found = null;
            foreach (var (e, sh) in sheets ?? new System.Collections.Generic.List<(GazEntry, TextureSheet)>())
                if (e.Index == PeopleSheet.Sheet) { found = sh; break; }
            var people = PeopleSheet.From(found);
            var img = people?.Sheet269?.RenderSprites("people");
            if (img == null || img.Width <= 0 || img.Height <= 0) return null;
            // The sheet renders on its own background; the people need everything round them to be clear.
            var rgba = (byte[])img.Rgba.Clone();
            for (int i = 0; i < rgba.Length; i += 4)
                if (rgba[i] == 24 && rgba[i + 1] == 16 && rgba[i + 2] == 28) rgba[i + 3] = 0;
            var tex = ImageTexture.CreateFromImage(Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgba8, rgba));
            return new GuestSprites(people, tex, img.Width, img.Height);
        }

        /// <summary>A guest's own quad, unlit and standing upright while it turns to face the camera.
        ///
        /// ⚠ THE TURN IS DONE HERE, NOT BY A BILLBOARD FLAG. Godot's own billboard modes left these lying
        /// flat on the paths — a quad drawn face-up on the ground, plainly visible in a capture — so the
        /// quad is aimed at the camera explicitly in <see cref="Draw"/>, where it can be read and fixed.</summary>
        public MeshInstance3D NewGuest()
        {
            var inst = new MeshInstance3D
            {
                // ⚠ SAY WHICH WAY THE QUAD FACES. A QuadMesh here comes out lying in the ground plane, its
                // face up, so the guests were drawn flat on the paths whatever the node's basis or the
                // material's billboard mode said. FaceZ stands it up; the basis in Draw turns it.
                Mesh = new QuadMesh { Size = new Vector2(0.2f, 0.4f), Orientation = PlaneMesh.OrientationEnum.Z },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoTexture = _atlas,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                    AlphaScissorThreshold = 0.5f,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            return inst;
        }

        /// <summary>Draw guest <paramref name="inst"/> as person <paramref name="block"/> walking in
        /// <paramref name="facing"/> at <paramref name="frame"/>, standing on <paramref name="feet"/>.
        /// The sprite's own offsets put the figure where the game puts it: they are measured from the
        /// pen, with the feet at the bottom, so the quad is raised by half its height less the offset.</summary>
        public void Draw(MeshInstance3D inst, int block, int facing, int frame, Vector3 feet, Vector3 cameraForward,
                         int pose = -1, int poseFrames = 1)
        {
            // `facing` arrives RELATIVE to the camera (person facing + camera octant); fold it to one of
            // the five stored drawings and mirror the half that reads the other way. A negative facing
            // means a POSE instead: those are facing-independent, so no fold and no mirror.
            // ⚠ THE MIRROR APPLIES TO THE POSES TOO. A pose table still has five facings -- they simply
            // hold the SAME ids -- so the sprite does not change with direction, but the draw still flips
            // it for relative facings 0..4. That is why a standing guest faces you (the idle art is a front
            // view) and yet is not identical from every angle: he faces you over one shoulder or the other.
            // Skipping the flip for poses, which is what "facing-independent" tempts you into, makes every
            // idle guest in the park face the same way.
            var fold = PeopleSheet.Fold(facing);
            bool mirror = fold.Mirror;
            int index = pose >= 0
                ? PeopleSheet.PoseSprite(block, pose, poseFrames, frame)
                : _people.WalkSprite(block, fold.Stored, frame);
            var sheet = _people.Sheet269;
            if (index < 0 || index >= sheet.Sprites.Count) return;
            var sp = sheet.Sprites[index];
            float w = sp.W * TilesPerTexel, h = sp.H * TilesPerTexel;
            if (inst.Mesh is QuadMesh q && q.Size.X != w) q.Size = new Vector2(w, h);
            else if (inst.Mesh is QuadMesh q2 && q2.Size.Y != h) q2.Size = new Vector2(w, h);
            if (inst.MaterialOverride is StandardMaterial3D m)
            {
                // ⚠⚠ PUT THE PEOPLE ATLAS BACK. A guest that has been a rider is still pointing at the
                // COMMON sheet, and drawing people-sheet UVs against it lands somewhere in the HUD font --
                // master's screenshot was a guest walking around as the letters "dF". Setting it here, in
                // the draw, is the only place that cannot be skipped; doing it on the way out of the seat
                // is not, because the way out is whatever the guest does next.
                if (m.AlbedoTexture != _atlas) m.AlbedoTexture = _atlas;
                // The sprite's rectangle in the baked sheet: its page's corner plus its own texels.
                int px = (sp.PageX - sheet.VramX) / 64 * TextureSheet.PageTexels + sp.U;
                int py = (sp.PageY - sheet.VramY) / TextureSheet.PageTexels * TextureSheet.PageTexels + sp.V;
                Inset(m, px, py, sp.W, sp.H, _atlasW, _atlasH);
            }
            // ⭐ FULL BILLBOARD, VERTICALLY TOO (master). This used to zero the Y of the view direction,
            // which is a yaw-only billboard: upright and correct from the game's own low camera, but it
            // leans away as soon as you look down at it, because the quad keeps standing on the world's up
            // while the camera no longer does. The console never showed that — its camera pitch is fixed —
            // so the port's free camera is what exposes it. The sprite is screen-aligned now: its +Z is the
            // view direction and its up is the camera's.
            //
            // ⚠ ANCHORED AT THE FEET, NOT THE CENTRE. The offset is along the sprite's OWN up, so the base
            // of the quad stays on the ground point as it tilts. Offsetting along world up instead sinks a
            // guest into the path the moment the camera pitches down.
            var towards = -cameraForward;
            if (towards.LengthSquared() < 1e-6f) towards = Vector3.Back;
            towards = towards.Normalized();
            var right = Vector3.Up.Cross(towards);
            // Looking straight down or up: world up and the view direction are parallel, so pick any right.
            right = right.LengthSquared() < 1e-6f ? Vector3.Right : right.Normalized();
            var up = towards.Cross(right).Normalized();
            // Mirrored by the QUAD, never by negating a UV scale: a negative scale samples one texel past
            // the rect, into whatever is beside it in the atlas (see DrawHead's note, and the HUD font).
            if (mirror) right = -right;
            inst.Transform = new Transform3D(new Basis(right, up, towards), feet + up * (h / 2));
            if (Debug && (_shouted++ % 600) == 0)
                GD.Print($"[guest] mesh {(inst.Mesh is QuadMesh qq ? qq.Orientation.ToString() + " " + qq.Size : inst.Mesh?.GetType().Name)} " +
                         $"basis x={inst.Transform.Basis.X} y={inst.Transform.Basis.Y} z={inst.Transform.Basis.Z} at {inst.Position} sprite {index} {sp.W}x{sp.H}");
        }

        /// <summary>The sprite's rectangle as UVs, pulled HALF A TEXEL inside it on every edge.
        ///
        /// ⚠⚠ WITHOUT THE INSET A SPRITE SAMPLES ITS NEIGHBOUR. A rect of W texels mapped across a quad's
        /// full 0..1 puts the last sample at exactly px + W -- one texel PAST the sprite -- and the quad's
        /// edges reach 0 and 1 exactly. In these atlases the neighbour is another sprite or, in the common
        /// sheet, the HUD FONT: heads and glyphs are interleaved in one block. The symptom is letters
        /// bleeding along one edge of a sprite, which is what master reported twice.
        ///
        /// ⚠ Nearest filtering does NOT save you: it picks the nearest texel to the sample point, and a
        /// sample sitting exactly on the boundary is nearest to the wrong side as readily as the right.
        /// Half a texel in is the standard fix and the only one that does not depend on how the rasteriser
        /// rounds.</summary>
        static void Inset(StandardMaterial3D m, int px, int py, int w, int h, int atlasW, int atlasH)
        {
            m.Uv1Scale = new Vector3((w - 1) / (float)atlasW, (h - 1) / (float)atlasH, 1);
            m.Uv1Offset = new Vector3((px + 0.5f) / atlasW, (py + 0.5f) / atlasH, 0);
        }

        /// <summary>READ 0x8003186C. Which way round the world is from where the camera sits, as an
        /// octant: the tile-plane direction that maps to SCREEN-RIGHT, quantised. Octant 0 means
        /// screen-right is the game's +x. This is ADDED to a person's own cardinal facing to get the
        /// direction relative to the viewer, which is what picks the drawing.
        ///
        /// ⚠ The port's world negates the game's y into Godot's z, so the game-plane components of the
        /// camera's right vector are (x, -z).</summary>
        public static int CameraOctant(Vector3 cameraRight)
        {
            float rx = cameraRight.X, ry = -cameraRight.Z;
            if (rx * rx + ry * ry < 1e-9f) return 0;
            return (int)Mathf.Round(-Mathf.Atan2(ry, rx) / (Mathf.Pi / 4)) & 7;
        }
    }
}
