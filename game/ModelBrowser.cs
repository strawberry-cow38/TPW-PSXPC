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

        // --- animation ---------------------------------------------------------------------------
        bool _playing;
        float _clock;                                   // in the file's own time units
        (int X, int Y, int Z)[] _posed;                 // null = draw the file's rest vertices
        Vector3 _fitCentre; float _fitScale = 1f;       // fixed at the REST pose, see Show()
        // ⭐ CULLED AS THE GAME CULLS, PER GROUP. The PS1 GPU has no back-face culling; the game does it in
        // software, and now we know exactly where: the model drawer's emitters (jump table 0x80011F34, picked by the
        // group's flags) test the GTE's NCLIP and drop a face that faces away UNLESS the group has flag bit 1
        // (MeshFace.DoubleSided), whose emitters skip the test. So by default single-sided groups are culled and
        // double-sided ones (fringes, flags, thin fins) drawn from both sides. This replaces "culling off by default",
        // master's call for fidelity when the game's rule was not known: with it off, the browser showed faces the
        // game never draws (the flag pole's end caps, the backs of single-sided parts).
        // The toggle culls EVERY face, the instrument for checking winding, which is how the inside-out models were caught.
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

        /// <summary>Toggle between the game's culling (single-sided groups only) and culling every face, which
        /// exposes winding faults.</summary>
        public void ToggleCull() { _cull = !_cull; Show(_index); }

        /// <summary>Toggle triangle order, so both windings can be compared against the same model rather
        /// than argued about. Whichever looks solid under culling is the right one.</summary>
        public void ToggleWinding() { _reverse = !_reverse; Show(_index); }

        /// <summary>Toggle textures, to see the vertex colours on their own.</summary>
        public void ToggleTextures() { _textured = !_textured; Show(_index); }

        /// <summary>Play or pause this model's animation.
        ///
        /// ⚠ A model with no tracks does not move, and "no animation in this model" must not look like
        /// "the player is broken". <see cref="StateLabel"/> says which, because the two are otherwise
        /// indistinguishable on screen and the wrong one of them sends someone debugging working code.</summary>
        public void TogglePlay() { _playing = !_playing; _clock = 0; Show(_index); }

        /// <summary>Pose at time 0 even while paused, for models that need it.
        ///
        /// ⚠ AN ANIMATED MODEL'S REST GEOMETRY CAN BE EMPTY. A quarter of all vertices in the animated
        /// models sit at exactly (0,0,0) in the file, and two models are 100% zero -- their shape exists
        /// only once the animation places it. Drawing the file's rest pose there draws a crumpled point,
        /// which looks like a broken parse rather than a model waiting to be posed. So a playable model
        /// is shown posed whether or not the clock is running.</summary>
        void RefreshPose()
        {
            if (_index < 0 || _index >= _meshes.Count) { _posed = null; return; }
            _posed = Playability == PlayState.Playable
                   ? MeshPose.Evaluate(_meshes[_index].Mesh, (int)_clock).Vertices
                   : null;
        }

        /// <summary>What this player can do with the model on screen.
        ///
        /// ⚠ THREE STATES, NOT TWO, AND CONFLATING THE LAST TWO IS A LIE THE USER CATCHES FIRST. Most
        /// models on the disc animate through BONES only, and nothing yet connects a bone to a vertex —
        /// so they have real animation data, this player poses their skeleton, and not one vertex moves.
        /// An "animation available" label on those promises motion that cannot happen, which is exactly
        /// how this was first reported: "nothing happens, the model stays the same".</summary>
        public enum PlayState { None, BonesOnly, Playable }

        public PlayState Playability
        {
            get
            {
                if (_index < 0 || _index >= _meshes.Count) return PlayState.None;
                var m = _meshes[_index].Mesh;
                if (m.Tracks == null) return PlayState.None;
                // ⚠ A BONE TRACK NOW REACHES VERTICES. This returned BonesOnly for anything driven
                // through the skeleton, which was true before the skin was decoded and false the moment
                // it was -- and it silently kept 291 models out of "next animated" after they had started
                // working. A capability check written against yesterday's limits outlives them.
                bool skinned = false;
                if (m.Skeleton != null)
                    foreach (var b in m.Skeleton.Bones)
                        if (b.SkinCount > 0) { skinned = true; break; }

                bool bones = false;
                foreach (var t in m.Tracks)
                {
                    // A track can move something if it writes vertices directly, drives a scatter source
                    // that has a binding behind it, or poses a bone that actually skins vertices.
                    if (t.Positions.Length > 0 &&
                        (t.Target == TrackTarget.Vertex ||
                         (t.Target == TrackTarget.ScatterSource && m.Binding != null && m.Binding.SourceCount > 0)))
                        return PlayState.Playable;
                    if (t.Keys.Length > 0)
                    {
                        if (skinned) return PlayState.Playable;
                        bones = true;
                    }
                }
                return bones ? PlayState.BonesOnly : PlayState.None;
            }
        }

        public bool HasAnimation => Playability == PlayState.Playable;

        public string StateLabel => $"cull {(_cull ? "ALL" : "as the game")} · order {(_reverse ? "flipped" : "correct")} · " +
                                    $"textures {(_textured ? "ON" : "off")} · " +
                                    Playability switch
                                    {
                                        PlayState.None      => "no animation in this model",
                                        PlayState.BonesOnly => "bone animation only — not playable yet",
                                        _ => _playing
                                             ? $"playing, t={(int)_clock} of {MeshPose.AnimationLength(_meshes[_index].Mesh)}"
                                             : $"animation available ({MeshPose.AnimationLength(_meshes[_index].Mesh)} long), paused",
                                    };

        public int Count => _meshes.Count;

        /// <summary>Show or hide the browser, taking the camera back when shown (the park view has its own).</summary>
        public void Activate(bool on)
        {
            Visible = on;
            if (on && _camera != null) _camera.Current = true;
        }

        /// <summary>The browser index of the first model in archive entry <paramref name="entry"/>, or -1.</summary>
        public int IndexOfEntry(int entry) => _meshes.FindIndex(x => x.Entry == entry);
        public int IndexOf(int entry, int sub) => _meshes.FindIndex(x => x.Entry == entry && x.Sub == sub);

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

        /// <summary>One parsed mesh by archive entry and sub-index, for a view that wants a SPECIFIC
        /// model rather than the browsing order. Reuses what Load already parsed; parsing the archive a
        /// second time would be a second chance to disagree with this one about what is in it.</summary>
        public bool TryGet(int entry, int sub, out TpwMesh mesh)
        {
            foreach (var (e, s2, m) in _meshes)
                if (e == entry && s2 == sub) { mesh = m; return true; }
            mesh = null;
            return false;
        }

        /// <summary>The texture sheets the models are cut from, for the same reason.</summary>
        public List<(GazEntry Entry, TextureSheet Sheet)> Sheets => _sheets;

        /// <summary>How many sub-meshes Load parsed, for diagnosing an empty browser.</summary>
        public int MeshCount => _meshes.Count;

        public void Show(int index)
        {
            if (_meshes.Count == 0) return;
            _index = ((index % _meshes.Count) + _meshes.Count) % _meshes.Count;
            var (entry, sub, m) = _meshes[_index];
            if (_posed == null || _posed.Length != m.VertexCount) RefreshPose();

            // Fit and build live in ModelMesh, so the browser, the advisor and anything else that puts
            // a model on screen share ONE path. ⚠ Extracted, not reimplemented -- see the note there.
            ModelMesh.Fit(m, out var centre, out float scale);
            var mesh = ModelMesh.Build(m, _posed, _sheets, _textured, _reverse, _cull, centre, scale, out var tex);
            _instance.Mesh = mesh;
            // ⭐ CULLING IS WHAT MAKES WINDING FALSIFIABLE AT ALL. With back faces drawn, a reversed triangle
            // renders identically to a correct one. Culling turned an unfalsifiable impression into an
            // observation, and master caught the inside-out models that way (see _cull for the default).
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
        /// (The vertex accessor these notes describe now lives in ModelMesh.Vert, so there is exactly one
        /// copy of the z negation. Two copies of a reflection is two chances to un-mirror only half the
        /// models.)


        /// <summary>Jump to the next model this player can actually move.
        ///
        /// ⚠ Only 29 of the 556 models on the disc have animation that reaches vertices. Stepping through
        /// with Next and pressing Play lands on a bone-only model 19 times out of 20, which reads as a
        /// broken player rather than as a rare feature -- and that is precisely how it was first reported.
        /// A direct jump is the difference between the feature being visible and being effectively absent.</summary>
        public void NextAnimated()
        {
            for (int step = 1; step <= _meshes.Count; step++)
            {
                int at = ((_index + step) % _meshes.Count + _meshes.Count) % _meshes.Count;
                int save = _index;
                _index = at;
                if (Playability == PlayState.Playable) { Show(at); return; }
                _index = save;
            }
        }

        public void Next() => Show(_index + 1);
        public void Prev() => Show(_index - 1);

        public override void _Process(double delta)
        {
            // Turn it slowly. A still model hides exactly the faults a browser exists to reveal -- an
            // inside-out winding or a collapsed axis both look fine from one angle.
            _spin += (float)delta * 0.6f;
            if (_instance != null) _instance.Rotation = new Vector3(0, _spin, 0);

            if (!_playing || _index < 0 || _index >= _meshes.Count) return;
            _clock += (float)delta * 30f;                    // a readable rate; the file's unit is not a second
            // Loop at the animation's OWN end. A fixed window cuts some animations short and leaves
            // others holding their last pose, and both look like a broken loop rather than a wrong length.
            int len = MeshPose.AnimationLength(_meshes[_index].Mesh);
            if (len > 0 && _clock >= len) _clock -= len; else if (len <= 0) _clock = 0;
            RefreshPose();
            Show(_index);
        }
    }
}
