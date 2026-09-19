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
    /// ⚠ NO TEXTURES YET, DELIBERATELY. Faces carry a CLUT and a page, but binding a page to file bytes is
    /// only proven for some of them, so this draws vertex colours. A textured view built on the unproven
    /// half would look far more finished than it is, which is the failure worth avoiding on an inspection
    /// tool above all others.</summary>
    public partial class ModelBrowser : Node3D
    {
        readonly List<(int Entry, int Sub, TpwMesh Mesh)> _meshes = new();
        MeshInstance3D _instance;
        Camera3D _camera;
        Label _info;
        int _index = -1;
        float _spin;

        public int Count => _meshes.Count;

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

            foreach (var e in gaz.Entries)
            {
                var bytes = gaz.Read(e);
                if (!MeshContainer.IsContainer(bytes)) continue;
                if (!MeshContainer.TryParse(bytes, out var c, out _)) continue;
                for (int i = 0; i < c.SubCount; i++)
                {
                    // Compressed sub-entries need LZSS, which is not ported. Skipped by name, not silently.
                    if (c.IsCompressed(i)) continue;
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
                float x = m.Vertices[i * 3], y = m.Vertices[i * 3 + 1], z = m.Vertices[i * 3 + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            var centre = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            float span = Mathf.Max(maxX - minX, Mathf.Max(maxY - minY, maxZ - minZ));
            float scale = span > 0.0001f ? 1.6f / span : 1f;

            var verts = new List<Vector3>(m.Faces.Count * 3);
            var cols = new List<Color>(m.Faces.Count * 3);
            foreach (var face in m.Faces)
            {
                // ⚠ A FACE INDEX OUT OF RANGE MUST SKIP THE FACE, NOT CRASH THE BROWSER. This is an
                // inspection tool for data we are still learning; it has to survive being wrong.
                if (face.I0 >= m.VertexCount || face.I1 >= m.VertexCount || face.I2 >= m.VertexCount) continue;
                // ⚠ PSX winds its triangles the other way round from Godot, so the order is reversed here.
                // Getting it wrong shows every model inside-out, which reads as a broken mesh rather than a
                // winding problem.
                foreach (int vi in new[] { face.I2, face.I1, face.I0 })
                {
                    var v = new Vector3(m.Vertices[vi * 3], -m.Vertices[vi * 3 + 1], m.Vertices[vi * 3 + 2]);
                    verts.Add((v - new Vector3(centre.X, -centre.Y, centre.Z)) * scale);
                    cols.Add(m.VertexColours.Length >= (vi + 1) * 3
                        ? Color.Color8(m.VertexColours[vi * 3], m.VertexColours[vi * 3 + 1], m.VertexColours[vi * 3 + 2])
                        : Colors.White);
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();

            var mesh = new ArrayMesh();
            if (verts.Count >= 3) mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            _instance.Mesh = mesh;
            _instance.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };

            if (_info != null)
            {
                var pages = new List<string>();
                foreach (var tp in m.TPages)
                {
                    var (px, py) = new MeshFace(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, tp).TPageOrigin;
                    pages.Add($"{px},{py}");
                }
                _info.Text =
                    $"model {_index + 1} of {_meshes.Count}   entry #{entry} sub {sub}\n" +
                    $"{m.VertexCount:n0} vertices   {m.Faces.Count:n0} faces   {m.BoneCount} bones   {m.TrackCount} anim tracks\n" +
                    $"texture pages: {(pages.Count > 0 ? string.Join("  ", pages) : "none")}";
            }
            GD.Print($"[tpw] model {_index + 1}/{_meshes.Count}: entry #{entry} sub {sub}, " +
                     $"{m.VertexCount} verts, {m.Faces.Count} faces");
        }

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
