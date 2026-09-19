using System.Collections.Generic;
using Godot;
using TPW.Data;
// ⚠ Godot has its own Mesh. Aliasing keeps both readable instead of fully-qualifying at every use.
using TpwMesh = TPW.Data.Mesh;

namespace TPWGodot
{
    /// <summary>Walks every mesh on the user's disc and draws it.
    ///
    /// ⭐ THIS EXISTS BECAUSE LOOKING IS THE INSTRUMENT THAT KEEPS WORKING. Every asset fault found so far —
    /// a 180-degree rotation, a residual mirror, the wrong channel order, audio at the wrong rate — was
    /// caught by a person looking or listening while every automated check passed. 531 meshes parsed with
    /// zero failures says the layout is self-consistent; it does not say a single one is shaped like a
    /// rollercoaster. Only a picture says that.
    ///
    /// ⭐ TEXTURED NOW, BECAUSE THE BINDING IS PROVEN. Faces carry a page, a palette and UVs; the texture
    /// sheets carry sprite tables that name the same three. 98.1% of all 54,619 faces sit inside a sprite with
    /// their page and palette, 94.9% in exactly one sheet (see ModelTexturing). The toggle turns textures off
    /// to show the vertex colours alone, which is what this browser drew before and is the better view for
    /// checking geometry.</summary>
    public partial class ModelBrowser : Node3D
    {
        readonly List<(int Entry, int Sub, TpwMesh Mesh)> _meshes = new();
        /// <summary>Entry → what the model is, from the attraction record in the same entry, named in English.</summary>
        readonly Dictionary<int, string> _names = new();
        MeshInstance3D _instance;
        Camera3D _camera;
        Label _info;
        int _index = -1;
        float _spin;
        // ⭐ CULLING OFF BY DEFAULT, BY MASTER'S CALL, and the reason is fidelity rather than taste: "in this era
        // the devs would have manually removed faces". The PS1 GPU has no back-face culling at all; a game culls
        // in software per polygon if it chooses to, and tinyclaw measured this one's park geometry at 88-90% one
        // handedness, so it does not cull everything. Culling everything here breaks the double-sided details
        // (fringes, flags, thin fins) that were modelled as single faces meant to be seen from both sides.
        // The toggle stays: culling ON is still the right instrument for checking winding, which is how the
        // inside-out models were caught.
        bool _cull = false;
        // ⭐ _reverse = false IS THE CORRECT ORDER. Its history: file order was settled by looking, with culling
        // ON (master reported every model inside out under the reversed order I had assumed). Then master saw
        // every model MIRRORED, which needs a reflection (ToGodot negates z), and a reflection reverses every
        // triangle. So the correct order is now the file's reversed, and "correct" is what false means here.
        //
        // ⚠ The first assumption survived for hours because the check that would have falsified it was
        // disabled. It was not defended by evidence, it was defended by being untestable.
        bool _reverse = false;
        bool _textured = true;
        List<(GazEntry Entry, TextureSheet Sheet)> _sheets = new();
        string _texInfo = "";

        /// <summary>Toggle back-face culling. Off hides winding faults; on exposes them.</summary>
        public void ToggleCull() { _cull = !_cull; Show(_index); }

        /// <summary>Toggle triangle order, so both windings can be compared against the same model rather
        /// than argued about. Whichever looks solid under culling is the right one.</summary>
        public void ToggleWinding() { _reverse = !_reverse; Show(_index); }

        /// <summary>Toggle textures, to see the vertex colours on their own.</summary>
        public void ToggleTextures() { _textured = !_textured; Show(_index); }

        public string StateLabel => $"cull {(_cull ? "ON" : "off")} · order {(_reverse ? "flipped" : "correct")} · " +
                                    $"textures {(_textured ? "ON" : "off")}";

        public int Count => _meshes.Count;

        /// <summary>Show or hide the browser, taking the camera back when shown (the park view has its own).</summary>
        public void Activate(bool on)
        {
            Visible = on;
            if (on && _camera != null) _camera.Current = true;
        }

        /// <summary>The browser index of the first model in archive entry <paramref name="entry"/>, or -1.</summary>
        public int IndexOfEntry(int entry) => _meshes.FindIndex(x => x.Entry == entry);

        public override void _Ready()
        {
            _instance = new MeshInstance3D();
            AddChild(_instance);

            // ⚠ LookAt REQUIRES THE NODE TO BE IN THE TREE — calling it first logs "Node not inside tree"
            // and silently leaves the camera unrotated, so the model is off-screen with no obvious cause.
            _camera = new Camera3D { Position = new Vector3(0, 0.6f, 2.6f) };
            AddChild(_camera);
            _camera.LookAt(Vector3.Zero, Vector3.Up);

            // Unshaded with vertex colours: no lighting rig to get wrong, and what is on screen is exactly
            // what is in the file rather than a lighting model's opinion of it.
            var env = new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(0.09f, 0.09f, 0.11f),
                },
            };
            AddChild(env);
        }

        public void SetInfoLabel(Label l) => _info = l;

        /// <summary>Parse every mesh in the archive. Off the main thread — this reads 16 MB and walks 556
        /// sub-entries, which is not something to do inside _Ready.</summary>
        public void Load(DiscReader disc)
        {
            var f = disc.Find(AssetSelfTest.AssetArchive);
            if (f == null || !GazArchive.TryParse(disc.ReadFile(f), out var gaz, out _)) return;
            _sheets = TextureSheet.FindAll(gaz);
            StringTable.TryRead(gaz, 0, out var english, out _);

            foreach (var e in gaz.Entries)
            {
                var bytes = gaz.Read(e);
                if (!MeshContainer.IsContainer(bytes)) continue;
                if (!MeshContainer.TryParse(bytes, out var c, out _)) continue;
                // ⭐ THE NAME COMES FROM THE GAME, NOT A GUESS: the attraction record sits in the same entry as
                // the model, and its text id indexes the English table.
                if (english != null && AttractionRecord.TryRead(bytes, c, out var rec) && !string.IsNullOrWhiteSpace(english[rec.TextId]))
                    _names[e.Index] = $"{english[rec.TextId]} ({rec.TypeName}, {rec.Width}x{rec.Depth} tiles)";
                for (int i = 0; i < c.SubCount; i++)
                {
                    // Compressed sub-entries (the model packs in entries 0 and 3) are expanded inside
                    // TryParseMesh by SubLz, so they show here like any other.
                    if (c.TryParseMesh(bytes, i, out var m, out _) && m.Faces.Count > 0)
                        _meshes.Add((e.Index, i, m));
                }
            }
        }

        public void Show(int index)
        {
            if (_meshes.Count == 0) return;
            _index = ((index % _meshes.Count) + _meshes.Count) % _meshes.Count;
            var (entry, sub, m) = _meshes[_index];

            // ⚠ AUTO-FIT RATHER THAN A GUESSED SCALE. Vertices are s16 in the game's own units and nobody has
            // established what one unit is. Fitting to the model's own bounds shows every mesh at a usable
            // size without inventing a conversion factor and then believing it.
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++)
            {
                float x = m.Vertices[i * 3], y = m.Vertices[i * 3 + 1], z = -m.Vertices[i * 3 + 2];   // see ToGodot
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            var centre = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            float span = Mathf.Max(maxX - minX, Mathf.Max(maxY - minY, maxZ - minZ));
            float scale = span > 0.0001f ? 1.6f / span : 1f;

            MeshTextures tex = _textured && _sheets.Count > 0 ? ModelTexturing.Build(m, _sheets) : null;

            // Two surfaces: faces with a texture, and faces without one (vertex colour only). Keeping them apart
            // means an untextured face can never pick up a stray texel from somebody else's tile.
            var tv = new List<Vector3>(); var tc = new List<Color>(); var tuv = new List<Vector2>();
            var pv = new List<Vector3>(); var pc = new List<Color>();
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var face = m.Faces[fi];
                // ⚠ A FACE INDEX OUT OF RANGE MUST SKIP THE FACE, NOT CRASH THE BROWSER. This is an
                // inspection tool for data we are still learning; it has to survive being wrong.
                if (face.I0 >= m.VertexCount || face.I1 >= m.VertexCount || face.I2 >= m.VertexCount) continue;
                int tile = tex != null ? tex.FaceTile[fi] : -1;
                // ⚠ NO Y FLIP. I assumed PSX vertices were Y-down and negated Y; master ran it and reported
                // every model upside down. They are already in Godot's sense.
                // The corners' UVs travel with them whichever order they are emitted in.
                // ⚠ THE Z NEGATION IN ToGodot IS A REFLECTION, AND A REFLECTION REVERSES EVERY TRIANGLE. So the
                // file's corner order, which faced outward before the fix, faces inward after it, and the order is
                // reversed here to keep the same faces front-facing. _reverse still flips it for comparison.
                var corners = !_reverse
                    ? new[] { (face.I2, face.U2, face.V2), (face.I1, face.U1, face.V1), (face.I0, face.U0, face.V0) }
                    : new[] { (face.I0, face.U0, face.V0), (face.I1, face.U1, face.V1), (face.I2, face.U2, face.V2) };
                foreach (var (vi, u, v) in corners)
                {
                    var pos = (ToGodot(m, vi) - centre) * scale;
                    var col = m.VertexColours.Length >= (vi + 1) * 3
                        ? Color.Color8(m.VertexColours[vi * 3], m.VertexColours[vi * 3 + 1], m.VertexColours[vi * 3 + 2])
                        : new Color(0.5f, 0.5f, 0.5f);
                    if (tile >= 0)
                    {
                        tv.Add(pos); tc.Add(col);
                        int ox = (tile % tex.TilesX) * ModelTexturing.Tile, oy = (tile / tex.TilesX) * ModelTexturing.Tile;
                        tuv.Add(new Vector2((ox + u) / (float)tex.Atlas.Width, (oy + v) / (float)tex.Atlas.Height));
                    }
                    else { pv.Add(pos); pc.Add(col); }
                }
            }

            var mesh = new ArrayMesh();
            var cull = _cull ? BaseMaterial3D.CullModeEnum.Back : BaseMaterial3D.CullModeEnum.Disabled;
            if (tv.Count >= 3)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex] = tv.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.Color] = tc.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.TexUV] = tuv.ToArray();
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                var atlas = ImageTexture.CreateFromImage(Image.CreateFromData(tex.Atlas.Width, tex.Atlas.Height, false,
                                                                              Image.Format.Rgba8, tex.Atlas.Rgba));
                var mat = new ShaderMaterial { Shader = PsxShader(_cull) };
                mat.SetShaderParameter("atlas", atlas);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
            }
            if (pv.Count >= 3)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                arrays[(int)Godot.Mesh.ArrayType.Vertex] = pv.ToArray();
                arrays[(int)Godot.Mesh.ArrayType.Color] = pc.ToArray();
                mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    CullMode = cull,
                });
            }
            _instance.Mesh = mesh;
            // ⭐ CULLING ON BY DEFAULT, BECAUSE IT IS WHAT MAKES WINDING FALSIFIABLE AT ALL. With back faces
            // drawn, a reversed triangle renders identically to a correct one. Culling on turned an
            // unfalsifiable impression into an observation, and master caught the inside-out models that way.
            _instance.MaterialOverride = null;

            _texInfo = tex == null ? "" :
                $"\ntextures: {tex.Atlas.Source} from sheet #{tex.MainSheetEntry}; faces {tex.Unique} by sprite, " +
                $"{tex.Ambiguous} shared (took the main sheet), {tex.Loose} by palette only, {tex.Unmatched} untextured";

            if (_info != null)
            {
                var pages = new List<string>();
                foreach (var tp in m.TPages)
                {
                    var (px, py) = new MeshFace(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, tp).TPageOrigin;
                    pages.Add($"{px},{py}");
                }
                _info.Text =
                    $"model {_index + 1} of {_meshes.Count}   entry #{entry} sub {sub}" +
                    (_names.TryGetValue(entry, out var name) ? $"   —   {name}" : "") + "\n" +
                    $"{m.VertexCount:n0} vertices   {m.Faces.Count:n0} faces   {m.BoneCount} bones   {m.TrackCount} anim tracks\n" +
                    $"texture pages: {(pages.Count > 0 ? string.Join("  ", pages) : "none")}   [{StateLabel}]" + _texInfo;
            }
            GD.Print($"[tpw] model {_index + 1}/{_meshes.Count}: entry #{entry} sub {sub}, " +
                     $"{m.VertexCount} verts, {m.Faces.Count} faces");
        }

        /// <summary>A file vertex in Godot's frame.
        ///
        /// ⚠ MIRRORED UNTIL Z WAS NEGATED. Master saw every model and its textures mirrored. Y already reads up
        /// (negating it turned them upside down, an earlier round of the same argument), so the file's frame is
        /// x right, y up, z INTO the screen, which is left-handed, and Godot's is right-handed. Negating z
        /// converts. That makes it a reflection, which is why the corner order is reversed with it.</summary>
        static Vector3 ToGodot(TpwMesh m, int vi) =>
            new Vector3(m.Vertices[vi * 3], m.Vertices[vi * 3 + 1], -m.Vertices[vi * 3 + 2]);

        /// <summary>The GPU's texture blend, as a shader. ⚠ Three rules, each the hardware's and not a style:
        /// palette colour 0 is TRANSPARENT (the atlas stores it as alpha 0, so discard); the texel is MODULATED by
        /// the vertex colour with 128 as neutral, so × 2 (the models' vertex colours run 48..255 around 128,
        /// darkening and brightening as baked light); and sampling is NEAREST, no filtering. The product is a
        /// display-space colour, so it is converted to linear on the way out, or Godot's output encode would
        /// brighten it a second time.</summary>
        static Shader PsxShader(bool cull)
        {
            string key = cull ? "back" : "disabled";
            if (_shaders.TryGetValue(key, out var sh)) return sh;
            sh = new Shader
            {
                Code = $@"shader_type spatial;
render_mode unshaded, cull_{key};
uniform sampler2D atlas : filter_nearest;
vec3 to_linear(vec3 c) {{
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}}
void fragment() {{
    vec4 t = texture(atlas, UV);
    if (t.a < 0.5) discard;
    ALBEDO = to_linear(clamp(t.rgb * COLOR.rgb * 2.0, 0.0, 1.0));
}}
",
            };
            _shaders[key] = sh;
            return sh;
        }
        static readonly Dictionary<string, Shader> _shaders = new();

        public void Next() => Show(_index + 1);
        public void Prev() => Show(_index - 1);

        public override void _Process(double delta)
        {
            // Turn it slowly. A still model hides exactly the faults a browser exists to reveal -- an
            // inside-out winding or a collapsed axis both look fine from one angle.
            _spin += (float)delta * 0.6f;
            if (_instance != null) _instance.Rotation = new Vector3(0, _spin, 0);
        }
    }
}
