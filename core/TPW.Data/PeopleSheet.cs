using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>The people: where every guest and every member of staff is drawn from.
    ///
    /// ⭐ THEY ARE SPRITES, NOT MODELS, AND THEY ARE ALL IN ONE SHEET. Archive entry <see cref="Sheet"/>
    /// is a texture sheet of 703 sprites on tpages 0x0C/0x0D/0x1C/0x1D — little figures 8 to 15 texels
    /// wide — each carrying its own palette, which is how one sheet holds a dozen people in different
    /// clothes.
    ///
    /// ⭐ WHICH SPRITES BELONG TO WHOM comes from the game's own table at 0x800DFFFC: twelve pairs of
    /// (archive entry of the person, first sprite of that person's block), looked up by entry at
    /// 0x800315A8. Those bases are <see cref="Blocks"/>. Inside a block the frames are grouped by what
    /// they are, and the WALK is a run of forty that all share one palette: FIVE stored facings of EIGHT
    /// frames, facing-major (<see cref="StoredFacings"/> x <see cref="WalkFrames"/>), with the other three
    /// directions drawn by MIRRORING three of the five.
    ///
    /// ⚠⚠ THIS WAS READ OFF THE PICTURES AS EIGHT-BY-FIVE AND IT WAS EXACTLY TRANSPOSED. "Five columns of
    /// a stride and eight rows that turn a circle" is what a 5x8 grid looks like when you read the axes
    /// the other way round, and a grid render cannot tell you which axis the data runs along -- only the
    /// code can. It shipped, and on screen one direction was right and the other seven pointed at random,
    /// because only the view whose stored index happened to coincide with its own frames survived.
    /// The layout here is now READ from 0x8002BDA8; findings/people-sprites.md has the trace.
    ///
    /// ⭐ AND WHICH PERSON A GUEST IS comes from the table at 0x800E002C: four worlds of fourteen archive
    /// entries, and the first EIGHT of each world are the guest bodies, indexed by the type byte the
    /// spawner rolls (V+0x61 = rand(8), 0x800926AC). Every world lists the same eight:
    /// 272, 272, 273, 273, 270, 270, 271, 271 — so there are FOUR guest bodies and each serves two of
    /// the eight types. The ninth entry is the world's own costumed character (267 / 265 / 259 / 275,
    /// one per theme), the next four are the staff (263, 264, 266, 274), and the last is a per-world
    /// entry in the 401..404 range. <see cref="GuestBlocks"/>, <see cref="StaffBlocks"/> and
    /// <see cref="CostumeBlocks"/> are those, as indices into <see cref="Blocks"/>.
    ///
    /// ⚠ Master, on the first park full of them: "u have a bunch of staff mixed in and maybe some
    /// costumed guests" — which is exactly what picking any of the twelve gives you.</summary>
    public sealed class PeopleSheet
    {
        /// <summary>The archive entry the sprites live in.</summary>
        public const int Sheet = 269;
        /// <summary>⚠⚠ FIVE STORED FACINGS OF EIGHT FRAMES, FACING-MAJOR -- not eight of five. The port
        /// had it exactly transposed, which is why one direction looked right and the other seven looked
        /// random: only the view whose stored index happened to land on its own frames was correct.
        ///
        /// The stored five are BACK, back-quarter, PROFILE (the art faces screen-RIGHT), front-quarter,
        /// FRONT. The other three directions are those same sprites X-mirrored, so a person is drawn from
        /// eight angles out of five drawings -- the same trick the rider heads use.
        /// findings/people-sprites.md.</summary>
        public const int StoredFacings = 5, WalkFrames = 8;
        const int Walk = StoredFacings * WalkFrames;

        /// <summary>READ 0x8002BB30 / 0x8003377C. Which stored drawing shows relative direction
        /// <paramref name="rel"/>, and whether it is drawn mirrored. ⚠ The MIRRORED half is 0..4, not
        /// 5..7 -- the stored profile faces screen-right, so the directions that read left are the flipped
        /// ones.</summary>
        public static (int Stored, bool Mirror) Fold(int rel)
        {
            rel &= 7;
            return rel < StoredFacings ? (rel, true) : (8 - rel, false);
        }

        /// <summary>The poses before the walk, at these offsets from the BLOCK's first sprite. READ from
        /// the person resource's own tables (findings/people-sprites.md §4): seven facing-independent
        /// poses in the 26 sprites before the walk, one palette each.
        ///
        /// ⚠ FACING-INDEPENDENT. There is one drawing per frame, not one per direction -- a guest stands
        /// and is sick the same way whichever way you look at it. So these are drawn without the fold and
        /// without the mirror.
        ///
        /// ⚠ Sprites 0..7 and 16..19 are DEAD ART: the animation ids that would select them are never
        /// written for a guest (census by two independent methods). Not exposed here, so nobody wires a
        /// pose the game cannot reach.</summary>
        public const int IdleFirst = 20, IdleFrames = 2;
        public const int VomitFirst = 22, VomitFrames = 4;

        /// <summary>A pose sprite: an offset from the block's own first sprite, not from the walk.</summary>
        public static int PoseSprite(int block, int first, int frames, int frame)
        {
            if (block < 0 || block >= Blocks.Length) return -1;
            return Blocks[block].Base + first + ((frame % frames) + frames) % frames;
        }

        /// <summary>READ 0x800932C8. A person's own facing is CARDINAL -- four directions, from the sign of
        /// its step, with x winning unless it is zero. ⚠ DO NOT SYNTHESISE EIGHT FROM THE VELOCITY: the
        /// quarter views exist only because the CAMERA's octant is added to this. A guest walking north is
        /// stored as north whatever angle you view it from.
        /// 0 = +y, 2 = -x, 4 = -y (and standing), 6 = +x, in the game's tile frame.</summary>
        public static int CardinalFacing(int dx, int dy)
            => dx == 0 ? (dy > 0 ? 0 : 4) : dx < 0 ? 2 : 6;

        /// <summary>The game's (person entry, first sprite) table, 0x800DFFFC, in its own order.</summary>
        public static readonly (int Entry, int Base)[] Blocks =
        {
            (272, 0), (273, 66), (270, 132), (271, 198), (267, 440), (265, 506),
            (275, 577), (259, 643), (263, 264), (264, 308), (266, 352), (274, 396),
        };

        /// <summary>The eight guest slots of 0x800E002C's per-world list, as indices into <see cref="Blocks"/>:
        /// entries 272, 272, 273, 273, 270, 270, 271, 271. A guest's type byte indexes this directly.</summary>
        public static readonly int[] GuestBlocks = { 0, 0, 1, 1, 2, 2, 3, 3 };

        /// <summary>The staff of every world (263, 264, 266, 274), as indices into <see cref="Blocks"/>.
        /// ⚠ USE THE NAMED ONES BELOW, NOT THIS IN ENUM ORDER — see <see cref="Mechanic"/>.</summary>
        public static readonly int[] StaffBlocks = { 8, 9, 10, 11 };

        /// <summary>Which sprite block each job wears (tinyclaw, read off the game): **263 mechanic, 264
        /// guard, 266 cleaner, 274 researcher**, as indices into <see cref="Blocks"/>.
        ///
        /// ⚠ NAMED BECAUSE THE ORDER IS A TRAP. These are not in job order, and code that indexed
        /// <see cref="StaffBlocks"/> by an enum happened to be right for the mechanic and the cleaner while
        /// silently drawing a GUARD as a researcher — the kind of wrong that looks fine until somebody knows
        /// the uniforms. Say the job, never the position.
        ///
        /// ⚠ AND THERE ARE FOUR OUTFITS FOR FIVE JOBS: an entertainer is not uniformed at all. Theirs is the
        /// per-world costume in <see cref="CostumeBlocks"/>.</summary>
        public const int Mechanic = 8, Guard = 9, Cleaner = 10, Researcher = 11;

        /// <summary>The costume each world has one of (267, 265, 259, 275), by world — the ENTERTAINER's
        /// outfit, and the one a guest takes on after buying from a costume shop (master: "entertainers are
        /// pictured here. those costumed guests. when guests buy costumes from costume shops they get that
        /// appearance"). It is a per-world look rather than a per-job uniform, which is why four uniforms
        /// cover five jobs.</summary>
        public static readonly int[] CostumeBlocks = { 4, 5, 7, 6 };

        readonly TextureSheet _sheet;
        /// <summary>The first walking sprite of each block, in <see cref="Blocks"/>' order.</summary>
        readonly int[] _walk;

        PeopleSheet(TextureSheet sheet, int[] walk) { _sheet = sheet; _walk = walk; }

        public TextureSheet Sheet269 => _sheet;
        public int Count => _walk.Length;

        /// <summary>Read the people out of <paramref name="archive"/>, or null when the sheet is missing.</summary>
        public static PeopleSheet Read(GazArchive archive)
        {
            if (archive == null || Sheet >= archive.Entries.Count) return null;
            return TextureSheet.TryParse(archive.Read(archive.Entries[Sheet]), out var sh, out _) ? From(sh) : null;
        }

        /// <summary>The people of a sheet a host has already parsed (entry <see cref="Sheet"/>).</summary>
        public static PeopleSheet From(TextureSheet sheet)
        {
            if (sheet == null || sheet.Sprites.Count == 0) return null;
            var walk = new int[Blocks.Length];
            for (int b = 0; b < Blocks.Length; b++) walk[b] = FindWalk(sheet, Blocks[b].Base);
            return new PeopleSheet(sheet, walk);
        }

        /// <summary>The walk inside a block: the first run of forty consecutive sprites that share a
        /// palette, at or after the block's first sprite. The frames before it are the figure's idle and
        /// gesture poses, which are drawn in several palettes and so break the run.
        ///
        /// ✅ It lands on the right sprite: a guest block is 26 pose sprites then 40 walk sprites, and this
        /// finds 26. ⚠ It cannot give the FRAMES PER FACING, which is 8 for guests but 9 for one costume
        /// (entry 265) -- the real source is the person resource's own table (findings/people-sprites.md
        /// §2), which the port does not parse. Fine while only guests are drawn; wrong the day a costume
        /// is.</summary>
        static int FindWalk(TextureSheet sheet, int from)
        {
            for (int i = from; i + Walk <= sheet.Sprites.Count; i++)
            {
                int clut = sheet.Sprites[i].Clut;
                int k = 1;
                while (k < Walk && sheet.Sprites[i + k].Clut == clut) k++;
                if (k == Walk) return i;
            }
            return from;
        }

        /// <summary>The sprite for a person of block <paramref name="block"/> showing STORED drawing
        /// <paramref name="stored"/> (0..4, from <see cref="Fold"/>) at <paramref name="frame"/> (0..7).
        /// READ 0x8002BDA8: the ids are facing-major, so a facing's eight frames are consecutive.</summary>
        public int WalkSprite(int block, int stored, int frame)
        {
            if (block < 0 || block >= _walk.Length) return -1;
            int s = stored < 0 ? 0 : stored >= StoredFacings ? StoredFacings - 1 : stored;
            return _walk[block] + s * WalkFrames + ((frame % WalkFrames) + WalkFrames) % WalkFrames;
        }

        /// <summary>Every sprite a walking person can be drawn as, for an atlas.</summary>
        public IEnumerable<int> WalkSprites()
        {
            foreach (int w in _walk)
                for (int i = 0; i < Walk; i++) yield return w + i;
        }
    }
}
