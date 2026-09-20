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
    /// they are, and the WALK is a run of forty that all share one palette: eight facings of five
    /// frames, in facing-major order (<see cref="Facings"/> x <see cref="WalkFrames"/>). That reading is
    /// off the pictures, not off the code — rendering a block as a grid shows five columns of a stride
    /// and eight rows that turn a full circle, and rendering the frames before it shows a figure waving
    /// its arms about on the spot.
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
        public const int Facings = 8, WalkFrames = 5;
        const int Walk = Facings * WalkFrames;

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
        /// gesture poses, which are drawn in several palettes and so break the run.</summary>
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

        /// <summary>The sprite for a person of block <paramref name="block"/> walking in
        /// <paramref name="facing"/> (0..7) at <paramref name="frame"/> (0..4).</summary>
        public int WalkSprite(int block, int facing, int frame)
        {
            if (block < 0 || block >= _walk.Length) return -1;
            return _walk[block] + (facing & (Facings - 1)) * WalkFrames + ((frame % WalkFrames) + WalkFrames) % WalkFrames;
        }

        /// <summary>Every sprite a walking person can be drawn as, for an atlas.</summary>
        public IEnumerable<int> WalkSprites()
        {
            foreach (int w in _walk)
                for (int i = 0; i < Walk; i++) yield return w + i;
        }
    }
}
