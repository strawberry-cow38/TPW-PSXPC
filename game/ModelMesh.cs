using System.Collections.Generic;
using Godot;
using TPW.Data;

using TpwMesh = TPW.Data.Mesh;

namespace TPWGodot
{
    /// <summary>Turning one parsed mesh into a Godot mesh, in ONE place.
    ///
    /// ⚠ EXTRACTED FROM ModelBrowser RATHER THAN REWRITTEN, and that is the whole point. A second
    /// builder would drift from this one silently: the winding, the Z reflection, the per-blend-mode
    /// semi-transparent buckets and the neutral colour for flat faces are all things that were got
    /// wrong once and fixed here, and a fresh copy would get to make every one of those mistakes
    /// again. Anything that needs a model on screen calls this.</summary>
    public static class ModelMesh
    {
        /// <summary>A file vertex in Godot's frame, posed if a pose was supplied.
        ///
        /// ⚠ Z IS NEGATED. Models read mirrored without it, and because the negation is a reflection it
        /// also reverses every triangle -- which is why the caller's "correct" winding is the file's
        /// order reversed.</summary>
        public static Vector3 Vert(TpwMesh m, (int X, int Y, int Z)[] posed, int vi)
        {
            if (posed != null && vi < posed.Length)
                return new Vector3(posed[vi].X, posed[vi].Y, -posed[vi].Z);
            return new Vector3(m.Vertices[vi * 3], m.Vertices[vi * 3 + 1], -m.Vertices[vi * 3 + 2]);
        }

        /// <summary>The model's bounds in Godot's frame, and the scale that fits it in a unit-ish box.
        ///
        /// ⚠ Take this from the REST pose and hold it. Refitting per animated frame rescales the model
        /// as it moves, so a limb swinging out shrinks the whole thing and the view cancels the motion.</summary>
        public static void Fit(TpwMesh m, out Vector3 centre, out float scale, float target = 1.6f)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++)
            {
                float x = m.Vertices[i * 3], y = m.Vertices[i * 3 + 1], z = -m.Vertices[i * 3 + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            centre = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            float span = Mathf.Max(maxX - minX, Mathf.Max(maxY - minY, maxZ - minZ));
            scale = span > 0.0001f ? target / span : 1f;
        }

        /// <summary>Build the Godot mesh. <paramref name="posed"/> may be null to draw the file's rest
        /// vertices.</summary>
        public static ArrayMesh Build(TpwMesh m, (int X, int Y, int Z)[] posed,
                                      List<(GazEntry Entry, TextureSheet Sheet)> sheets,
                                      bool textured, bool reverse, bool cull,
                                      Vector3 centre, float scale, out MeshTextures tex)
        {
            tex = null;
            tex = textured && sheets != null && sheets.Count > 0 ? ModelTexturing.Build(m, sheets) : null;

            // Two surfaces: faces with a texture, and faces without one (vertex colour only). Keeping them apart
            // means an untextured face can never pick up a stray texel from somebody else's tile.
            // Textured faces go to one bucket per way the GPU draws them: -1 solid, 0-3 semi-transparent by the
            // page's blend mode (MeshFace.SemiTransparent / BlendMode).
            var buckets = new SortedDictionary<int, (List<Vector3> V, List<Color> C, List<Vector2> UV)>();
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
                // reversed here to keep the same faces front-facing. the reverse flag still flips it for comparison.
                var corners = !reverse
                    ? new[] { (face.I2, face.U2, face.V2), (face.I1, face.U1, face.V1), (face.I0, face.U0, face.V0) }
                    : new[] { (face.I0, face.U0, face.V0), (face.I1, face.U1, face.V1), (face.I2, face.U2, face.V2) };
                foreach (var (vi, u, v) in corners)
                {
                    var pos = (Vert(m, posed, vi) - centre) * scale;
                    var col = m.VertexColours.Length >= (vi + 1) * 3
                        ? Color.Color8(m.VertexColours[vi * 3], m.VertexColours[vi * 3 + 1], m.VertexColours[vi * 3 + 2])
                        : new Color(0.5f, 0.5f, 0.5f);
                    if (tile >= 0)
                    {
                        // Bucket by how the GPU blends it and whether the game culls it: (blend + 1) * 2 + both sides.
                        int key = ((face.SemiTransparent ? face.BlendMode : -1) + 1) * 2 + (face.DoubleSided ? 1 : 0);
                        if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = (new List<Vector3>(), new List<Color>(), new List<Vector2>());
                        b.V.Add(pos);
                        // A flat face's builder writes 0x808080, the neutral colour: the texel unshaded.
                        b.C.Add(face.Flat ? new Color(128 / 255f, 128 / 255f, 128 / 255f) : col);
                        int ox = (tile % tex.TilesX) * ModelTexturing.Tile, oy = (tile / tex.TilesX) * ModelTexturing.Tile;
                        b.UV.Add(new Vector2((ox + u) / (float)tex.Atlas.Width, (oy + v) / (float)tex.Atlas.Height));
                    }
                    else { pv.Add(pos); pc.Add(col); }
                }
            }

            var mesh = new ArrayMesh();
            var cullMode = cull ? BaseMaterial3D.CullModeEnum.Back : BaseMaterial3D.CullModeEnum.Disabled;
            if (buckets.Count > 0)
            {
                var atlas = ImageTexture.CreateFromImage(Image.CreateFromData(tex.Atlas.Width, tex.Atlas.Height, false,
                                                                              Image.Format.Rgba8, tex.Atlas.Rgba));
                void AddSurface((List<Vector3> V, List<Color> C, List<Vector2> UV) b, Shader shader)
                {
                    if (b.V.Count < 3) return;
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Godot.Mesh.ArrayType.Max);
                    arrays[(int)Godot.Mesh.ArrayType.Vertex] = b.V.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.Color] = b.C.ToArray();
                    arrays[(int)Godot.Mesh.ArrayType.TexUV] = b.UV.ToArray();
                    mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                    var mat = new ShaderMaterial { Shader = shader };
                    mat.SetShaderParameter("atlas", atlas);
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
                }
                // ⭐ SEMI-TRANSPARENCY IS THE GPU'S, PER TEXEL. A semi-transparent face draws its texels without
                // the blend bit solid and blends only the ones with it, so each such bucket is drawn twice.
                foreach (var (key, b) in buckets)
                {
                    int blend = key / 2 - 1;
                    bool culled = cull || (key & 1) == 0;
                    if (blend < 0) { AddSurface(b, PsxShading.Shader(culled)); continue; }
                    AddSurface(b, PsxShading.SemiTransparentShader(blend, false, culled));
                    AddSurface(b, PsxShading.SemiTransparentShader(blend, true, culled));
                }
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
                    CullMode = cullMode,
                });
            }
            return mesh;
        }
    }
}
