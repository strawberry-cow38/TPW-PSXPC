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
        public void Draw(MeshInstance3D inst, int block, int facing, int frame, Vector3 feet, Vector3 cameraForward)
        {
            int index = _people.WalkSprite(block, facing, frame);
            var sheet = _people.Sheet269;
            if (index < 0 || index >= sheet.Sprites.Count) return;
            var sp = sheet.Sprites[index];
            float w = sp.W * TilesPerTexel, h = sp.H * TilesPerTexel;
            if (inst.Mesh is QuadMesh q && q.Size.X != w) q.Size = new Vector2(w, h);
            else if (inst.Mesh is QuadMesh q2 && q2.Size.Y != h) q2.Size = new Vector2(w, h);
            if (inst.MaterialOverride is StandardMaterial3D m)
            {
                // The sprite's rectangle in the baked sheet: its page's corner plus its own texels.
                int px = (sp.PageX - sheet.VramX) / 64 * TextureSheet.PageTexels + sp.U;
                int py = (sp.PageY - sheet.VramY) / TextureSheet.PageTexels * TextureSheet.PageTexels + sp.V;
                m.Uv1Scale = new Vector3(sp.W / (float)_atlasW, sp.H / (float)_atlasH, 1);
                m.Uv1Offset = new Vector3(px / (float)_atlasW, py / (float)_atlasH, 0);
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
            inst.Transform = new Transform3D(new Basis(right, up, towards), feet + up * (h / 2));
            if (Debug && (_shouted++ % 600) == 0)
                GD.Print($"[guest] mesh {(inst.Mesh is QuadMesh qq ? qq.Orientation.ToString() + " " + qq.Size : inst.Mesh?.GetType().Name)} " +
                         $"basis x={inst.Transform.Basis.X} y={inst.Transform.Basis.Y} z={inst.Transform.Basis.Z} at {inst.Position} sprite {index} {sp.W}x{sp.H}");
        }

        /// <summary>Which of the eight drawn facings a guest walking (dx, dz) shows the camera, given
        /// where the camera is looking: row 0 is walking away from it, and they go round from there.</summary>
        public static int FacingFor(float dx, float dz, Vector3 cameraForward)
        {
            var f = new Vector2(cameraForward.X, cameraForward.Z);
            if (f.LengthSquared() < 1e-6f) f = new Vector2(0, -1);
            f = f.Normalized();
            var right = new Vector2(-f.Y, f.X);
            var move = new Vector2(dx, dz);
            if (move.LengthSquared() < 1e-6f) return 0;
            move = move.Normalized();
            float a = Mathf.Atan2(move.Dot(right), move.Dot(f));
            return (int)Mathf.Round(a / (Mathf.Pi / 4)) & 7;
        }
    }
}
