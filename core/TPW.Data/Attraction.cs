using System;
using System.Collections.Generic;

namespace TPW.Data
{
    /// <summary>An attraction's definition record, from its archive entry (a 0x96 container whose header +0x14
    /// points at it; the same entry carries the model). Layout in findings/rides.md §1.3.
    ///
    /// ⭐ FROM THE GAME'S CODE: footprint width +0x08 (0x8006A798) and depth +0x0A (0x8006A7A4); the entrance tile
    /// offset +0x0C and the exit tile offset +0x10 (s16 x, z inside the footprint; -1 for none: 0x80066004 /
    /// 0x80065FD8), the way each faces +0x14 / +0x15 (0x80065FF8 / 0x80065FCC); the record body's length +0x1C, and
    /// right after the body the ground pad (0x8006A7C8): per footprint tile, row by row, s16 sprite in the world's
    /// ground sheet (negative: no ground drawn there) and u16 flags (turns in bits 0-1, mirror V bit 2, mirror U
    /// bit 3: 0x80066024 / 0x80066030 / 0x80066044).</summary>
    /// <summary>One upgrade level of a ride: the 13-word block at record +0x24 + 0x34xL (rides.md §1.3,
    /// slots 95..102 and 0x8009F470 / 0x8009F524).
    ///
    /// ⭐ AN UPGRADE IS MOSTLY A WEAR REDUCTION. The multiplier goes 5 at level 0 to 3 to 2, which more
    /// than halves how fast the ride wears out, on top of more seats and a reliability reset. The seat
    /// count is the more obvious sell and the smaller effect.</summary>
    public readonly struct RideLevel
    {
        /// <summary>+0x08: 5 / 3 / 2 for most rides, 7 for tour rides and Rock 'n Roll. Multiplies the
        /// wear rate directly (TPW.Sim.RideWear).</summary>
        public readonly int WearMultiplier;
        /// <summary>+0x0C: seats, and so the denominator of the load fraction the wear rate uses.</summary>
        public readonly int MaxSeats;
        /// <summary>+0x10: 45..100. One is spent per 15 points of reliability lost, and NOTHING restores
        /// it - see TPW.Sim.RideWear. Read once, at placement (0x8009F524).</summary>
        public readonly int Lifetime;
        /// <summary>+0x14 / +0x18: the speed slider's range. 1..100, except tour rides which start at 80.</summary>
        public readonly int SpeedMin, SpeedMax;
        /// <summary>+0x1C / +0x20: the duration slider's range, in animation phases per run. 1 for a
        /// coaster; up to 60 for the bouncers.</summary>
        public readonly int CyclesMin, CyclesMax;
        /// <summary>+0x24 and +0x28 (record +0x48 and +0x4C at level L; research.md §3.2, READ).
        ///
        /// ⭐ THE RESEARCH FIELDS, AND THEY WERE THE ONLY TWO THIS STRUCT SKIPPED. Everything between
        /// +0x08 and +0x2C was already read; these two sat in the gap and the research system could
        /// not run without them, because TPW.Sim.IResearchCatalogueWorld.ReadResearchLevel is exactly
        /// this pair.
        ///
        /// ⚠ TIER IS AN ORDINAL, NOT A COST. research.md §0 records that 0x8006A9A8 was once called a
        /// "required research cost"; it indexes five bins. WORK is the thing progress is measured
        /// against. ⚠ TIER 0 WITH NONZERO WORK AUTO-UNLOCKS — that rule lives in ResearchSystem, not
        /// here, and it is why a zero here is load-bearing rather than missing data.</summary>
        public readonly int ResearchTier, ResearchWork;

        /// <summary>+0x2C: level 0 is the build price, 1 and 2 are the upgrade prices. In pounds; the
        /// bank stores ten times this (economy.md §4.6).</summary>
        public readonly int Price;

        public RideLevel(int wearMultiplier, int maxSeats, int lifetime, int speedMin, int speedMax,
                         int cyclesMin, int cyclesMax, int researchTier, int researchWork, int price)
        {
            WearMultiplier = wearMultiplier; MaxSeats = maxSeats; Lifetime = lifetime;
            SpeedMin = speedMin; SpeedMax = speedMax; CyclesMin = cyclesMin; CyclesMax = cyclesMax;
            ResearchTier = researchTier; ResearchWork = researchWork;
            Price = price;
        }

        /// <summary>Where the duration slider starts: half the maximum (rides.md §4.3), so 5 for most
        /// rides. ⚠ DERIVED from the report's "default cycles max / 2", not read from a save.</summary>
        public int DefaultCycles => Math.Max(CyclesMin, CyclesMax / 2);
    }

    /// <summary>A shop's record body (type 4, 0x18 bytes; rides.md §1.3, re-read for the purchase port).
    ///
    /// ⚠ THE PRODUCT PARAMETERS ARE BYTES, NOT u16s. rides.md prints +0x30/+0x32/+0x34/+0x36 as four
    /// u16s "not resolved" (7, 25, 5, 10 for Fries), and on the Drinks Shop that view shows 10240 at
    /// +0x32. The getters are `lbu`: 0x800B711C at +0x30, 0x800B70F8 at +0x32, 0x800B70EC at +0x33,
    /// 0x800B70E0 at +0x34, 0x800B70D4 at +0x36 -- so 10240 is +0x32 = 0 and +0x33 = 40, two different
    /// numbers, and +0x31/+0x35/+0x37 are read by nothing. Each field says which getter and what the
    /// purchase routine 0x8008E5EC does with it (TPW.Sim.ShopProduct carries the same five).</summary>
    public readonly struct ShopFields
    {
        /// <summary>+0x2C u16: the sale price the shop opens with (0x800B7110 → A+0x88 through 0x800B7140).</summary>
        public readonly int DefaultPrice;
        /// <summary>+0x2E u16 (0x800B7104): what the unit cost is scaled from (economy.md §4.2).</summary>
        public readonly int UnitCost;
        /// <summary>+0x30 u8 (0x800B711C): 0..7, the product kind the purchase dispatches on.</summary>
        public readonly int Kind;
        /// <summary>+0x32 u8 (0x800B70F8, shop vtable slot 56): weight of need A in the want; taken off
        /// need A by the food kinds.</summary>
        public readonly int NeedAValue;
        /// <summary>+0x33 u8 (0x800B70EC, slot 55): weight of need B in the want; taken off need B by the
        /// drink kind.</summary>
        public readonly int NeedBValue;
        /// <summary>+0x34 u8 (0x800B70E0): weight of (100 − happiness) in the want; multiplies the
        /// happiness a purchase pays.</summary>
        public readonly int HappinessValue;
        /// <summary>+0x36 u8 (0x800B70D4): weight of nausea against the want; the nausea a food purchase adds.</summary>
        public readonly int NauseaValue;

        public ShopFields(int defaultPrice, int unitCost, int kind, int needAValue, int needBValue, int happinessValue, int nauseaValue)
        {
            DefaultPrice = defaultPrice; UnitCost = unitCost; Kind = kind; NeedAValue = needAValue;
            NeedBValue = needBValue; HappinessValue = happinessValue; NauseaValue = nauseaValue;
        }
    }

    /// <summary>A sideshow's record body (type 5, 0x14 bytes; rides.md §1.3, READ at 0x800B79E0 /
    /// 0x800B79D4 / 0x800B79C8): copied to the live object's A+0x84 / A+0x86 / A+0x78 at placement.</summary>
    public readonly struct SideShowFields
    {
        /// <summary>+0x2C u16, pounds.</summary>
        public readonly int PlayPrice;
        /// <summary>+0x2E u16, a percentage.</summary>
        public readonly int WinChance;
        /// <summary>+0x30 u8, pounds. ⚠ A byte: no sideshow can offer more than £255.</summary>
        public readonly int Prize;

        public SideShowFields(int playPrice, int winChance, int prize) { PlayPrice = playPrice; WinChance = winChance; Prize = prize; }
    }

    public sealed class AttractionDefinition
    {
        public int Entry, Type, NameId, Width, Depth, EntranceFacing, ExitFacing, Price;

        /// <summary>Record +0x18 (rides.md §1.3, READ 0x800A0B1C): rides 40..95, coasters 90/95, shops
        /// 20, features 0.
        ///
        /// ⭐ THIS IS THE RIDE'S "SLOT 53", the number a guest matches its own taste against - both when
        /// choosing what to go on (the ride score) and when working out how much it enjoyed it
        /// (behaviour.md §2.4's `|pref − ride.intensity|`). Two reports name it independently, which is
        /// why it can be wired without guessing: rides.md reads the field, behaviour.md names the slot.</summary>
        public int BaseIntensity;

        /// <summary>Record +0x2E on a FEATURE (type 2): four one-bit properties, each with its own
        /// accessor in the same run of three-instruction leaves — every one of them `lbu v0,46(a0)`
        /// followed by an `andi`:
        ///
        /// <list type="bullet">
        /// <item>bit 0 (0x80024348): guests may use it — <see cref="UsableByGuests"/>.</item>
        /// <item>bit 1 (0x8002433C): <see cref="StaffMayRest"/>, the flag state 49 searches on.</item>
        /// <item>bit 2 (0x80024330): not established.</item>
        /// <item>bit 3 (0x80024324): not established.</item>
        /// </list>
        ///
        /// ⚠ THE OTHER TWO BITS ARE DELIBERATELY NOT NAMED. Their accessors exist and are read
        /// somewhere; what they mean is not established, and a plausible name on a bit is worse than a
        /// number, because the next reader takes the name as the finding.</summary>
        public int FeatureFlags;

        /// <summary>Record +0x2E bit 1 on a FEATURE: staff may rest here (READ, 0x8002433C).
        ///
        /// ⭐ THIS IS THE THING STAFF STATE 49 LOOKS FOR. behaviour.md §3.1 has "nearest object in list
        /// 0x80053248 whose 0x8002433C-flag and status byte are set", and marks the object a GUESS
        /// ("a bench/staff room"). The flag itself is not a guess: it is this bit, read by a leaf that
        /// does nothing else. Without somewhere carrying it, a tired staff member goes back to work
        /// instead of recovering and its tiredness only ever climbs.</summary>
        public bool StaffMayRest => (FeatureFlags & 2) != 0;

        /// <summary>Record +0x2E bit 0 on a FEATURE (type 2): guests may use it. This is what the ride
        /// score calls "slot 54", which gates both of its desire terms.
        ///
        /// ⚠ FOR A REAL RIDE SLOT 54 IS ALWAYS ZERO (rides.md §0 item 1: slot 54 = 0x8009C2A0 = returns
        /// 0, READ), so the two desire terms are dead for rides and only features ever get them. That is
        /// why the ride score can be wired at all without knowing what slots 55 and 56 hold.</summary>
        public bool UsableByGuests;
        public (int X, int Z)? Entrance, Exit;
        public (short Sprite, ushort Flags)[] Pad = Array.Empty<(short, ushort)>();

        /// <summary>The three upgrade levels of a ride, or empty for everything else (rides.md §1.3).</summary>
        public RideLevel[] Levels = Array.Empty<RideLevel>();

        /// <summary>READ: coaster connection getters 0x800B1EB4/1EF4, heights 0x800B1EA8/1EE8,
        /// launch speed 0x800B1E90, direction bits 0x800B1E9C/1ED4. Not guest doors.</summary>
        public CoasterDefinition Coaster;

        /// <summary>A shop's product block, for type 4 only.</summary>
        public ShopFields? Shop;

        /// <summary>A sideshow's game, for type 5 only.</summary>
        public SideShowFields? SideShow;

        /// <summary>The level a ride is placed at.</summary>
        public RideLevel Level0 => Levels.Length > 0 ? Levels[0] : default;

        /// <summary>Types (findings/rides.md §1.1): 1 coaster, 2 feature, 3 flat ride, 4 shop, 5 sideshow, 6 track
        /// ride, 7 tour ride.</summary>
        public bool IsRide => Type is 1 or 3 or 6 or 7;

        public static AttractionDefinition Read(int entry, byte[] d)
        {
            if (!MeshContainer.TryParse(d, out var c, out _)) return null;
            int r = (int)c.RecordOffset;
            if (r <= 0 || r + 0x24 > d.Length) return null;
            int type = BitConverter.ToInt32(d, r);
            if (type < 1 || type > 8) return null;
            var a = new AttractionDefinition
            {
                Entry = entry, Type = type, NameId = BitConverter.ToInt32(d, r + 4),
                Width = d[r + 8], Depth = d[r + 0x0A],
                EntranceFacing = d[r + 0x14] & 3, ExitFacing = d[r + 0x15] & 3,
            };
            if (r + 0x1C <= d.Length) a.BaseIntensity = BitConverter.ToInt32(d, r + 0x18);
            if (type == 1 && r + 0xD4 <= d.Length) a.Coaster = CoasterDefinition.Read(d.AsSpan(r));
            if (type == 2 && r + 0x2F <= d.Length)
            {
                a.FeatureFlags = d[r + 0x2E];
                a.UsableByGuests = (a.FeatureFlags & 1) != 0;
            }
            short ex = BitConverter.ToInt16(d, r + 0x0C), ez = BitConverter.ToInt16(d, r + 0x0E);
            short xx = BitConverter.ToInt16(d, r + 0x10), xz = BitConverter.ToInt16(d, r + 0x12);
            if (ex >= 0 && ez >= 0) a.Entrance = (ex, ez);
            if (xx >= 0 && xz >= 0) a.Exit = (xx, xz);
            // Build price: level 0's block for the rides (+0x24 + 0x2C), +0x20 for the rest (rides.md §1.3).
            int priceAt = a.IsRide ? r + 0x24 + 0x2C : r + 0x20;
            if (priceAt + 4 <= d.Length) a.Price = BitConverter.ToInt32(d, priceAt);
            // ⭐ THE PER-LEVEL BLOCK, which is where a ride's real numbers live: how fast it wears, how
            // many it seats, how long it lasts and what its sliders may be set to. Nothing could read
            // them before, so wear, throughput and upgrades all had to be guessed at.
            //
            // ⚠ THE DATA HOLDS THREE AND THE CODE ALLOWS A FOURTH. 0x8009C56C increments the level
            // while it is < 3, so a third upgrade reads 0x34 bytes past the record into the model - for
            // a Crazy Ape, a £65,819 upgrade with 65,818 seats. Three are parsed here deliberately; a
            // port that offers a fourth would be reproducing a buffer overrun, not a feature.
            if (a.IsRide)
            {
                var levels = new List<RideLevel>();
                for (int L = 0; L < 3; L++)
                {
                    int b = r + 0x24 + 0x34 * L;
                    if (b + 0x30 > d.Length) break;
                    levels.Add(new RideLevel(
                        wearMultiplier: BitConverter.ToInt32(d, b + 0x08),
                        maxSeats:       BitConverter.ToInt32(d, b + 0x0C),
                        lifetime:       BitConverter.ToInt32(d, b + 0x10),
                        speedMin:       BitConverter.ToInt32(d, b + 0x14),
                        speedMax:       BitConverter.ToInt32(d, b + 0x18),
                        cyclesMin:      BitConverter.ToInt32(d, b + 0x1C),
                        cyclesMax:      BitConverter.ToInt32(d, b + 0x20),
                        researchTier:   BitConverter.ToInt32(d, b + 0x24),
                        researchWork:   BitConverter.ToInt32(d, b + 0x28),
                        price:          BitConverter.ToInt32(d, b + 0x2C)));
                }
                a.Levels = levels.ToArray();
            }
            // The shop body runs to +0x38 and the sideshow body to +0x34 (rides.md §1.3); the last byte read
            // is +0x36 and +0x30 respectively. Bytes, at the getters' own offsets -- see ShopFields.
            if (type == 4 && r + 0x38 <= d.Length)
                a.Shop = new ShopFields(BitConverter.ToUInt16(d, r + 0x2C), BitConverter.ToUInt16(d, r + 0x2E),
                                        d[r + 0x30], d[r + 0x32], d[r + 0x33], d[r + 0x34], d[r + 0x36]);
            if (type == 5 && r + 0x31 <= d.Length)
                a.SideShow = new SideShowFields(BitConverter.ToUInt16(d, r + 0x2C), BitConverter.ToUInt16(d, r + 0x2E), d[r + 0x30]);

            int body = BitConverter.ToInt32(d, r + 0x1C);
            int pad = r + 0x20 + body, n = a.Width * a.Depth;
            if (body >= 0 && n > 0 && pad + n * 4 <= d.Length)
            {
                a.Pad = new (short, ushort)[n];
                for (int i = 0; i < n; i++) a.Pad[i] = (BitConverter.ToInt16(d, pad + i * 4), BitConverter.ToUInt16(d, pad + i * 4 + 2));
            }
            return a;
        }

        /// <summary>The footprint on the map at a quarter-turn rotation: width and depth swap for 1 and 3 (slots 12 / 14).</summary>
        public (int W, int D) Footprint(int rot) => (rot & 1) == 0 ? (Width, Depth) : (Depth, Width);

        /// <summary>A tile offset inside the footprint, turned (0x800634B8).</summary>
        public (int X, int Z) Rotate(int x, int z, int rot) => (rot & 3) switch
        {
            1 => (z, Width - 1 - x),
            2 => (Width - 1 - x, Depth - 1 - z),
            3 => (Depth - 1 - z, x),
            _ => (x, z),
        };

        /// <summary>The tile just outside the entrance (0x800635E8) and the exit (0x800636F0): the door's own tile, then
        /// one step out of the footprint the way it faces (facing + rotation: 0 north, z - 1; 1 west, x - 1; 2 south,
        /// z + 1; 3 east, x + 1). The placement ghost's door markers stand here, placing lays the queue piece or path
        /// here, and a ride's queue starts here.</summary>
        public (int X, int Z)? EntranceTile(int ox, int oz, int rot) => Door(Entrance, EntranceFacing, ox, oz, rot);
        public (int X, int Z)? ExitTile(int ox, int oz, int rot) => Door(Exit, ExitFacing, ox, oz, rot);

        /// <summary>The entrance's and exit's own tiles (0x80062E48 / 0x80062FF8): the record's offset turned, which is
        /// always on the footprint's edge (every flat ride, shop and sideshow record READ: the offsets are inside
        /// the footprint). Placing makes them 7 and 8.</summary>
        public (int X, int Z)? EntranceDoor(int ox, int oz, int rot) => Inside(Entrance, ox, oz, rot);
        public (int X, int Z)? ExitDoor(int ox, int oz, int rot) => Inside(Exit, ox, oz, rot);

        (int X, int Z)? Inside((int X, int Z)? offset, int ox, int oz, int rot)
        {
            if (offset is not { } o) return null;
            var (x, z) = Rotate(o.X, o.Z, rot);
            return (x + ox, z + oz);
        }

        /// <summary>Which way the entrance or exit faces on the map (0 north … 3 east), for its marker's turn.</summary>
        public int EntranceTurn(int rot) => (EntranceFacing + rot) & 3;
        public int ExitTurn(int rot) => (ExitFacing + rot) & 3;

        (int X, int Z)? Door((int X, int Z)? offset, int facing, int ox, int oz, int rot)
        {
            if (offset is not { } o) return null;
            var (x, z) = Rotate(o.X, o.Z, rot);
            x += ox; z += oz;
            switch ((facing + rot) & 3)
            {
                case 0: z -= 1; break;
                case 1: x -= 1; break;
                case 2: z += 1; break;
                default: x += 1; break;
            }
            return (x, z);
        }
    }

    /// <summary>Each world's attractions for the port's picker: flat rides, shops, sideshows and features, as
    /// archive entries. From the theme tables 0x8002ED50 builds (findings/rides_themes.json).
    ///
    /// ⚠ BOTH SETS AT ONCE. A park draws from ONE of the two sets the theme holds (the level manager's +4, which
    /// the scenario picks and nothing traced writes), so the game offers 8 rides where this offers 14. The port
    /// shows the union until the scenario loader is read; the entries themselves are the game's.
    ///
    /// ⚠ THE COASTERS, TRACK RIDES AND TOUR RIDES ARE DERIVED, NOT READ. Their arrays in the theme table are
    /// filled at run time by the scenario loader (rides.md §1.2) rather than built from constants, so which ones
    /// a given scenario offers is not in TPW.BIN. What IS certain is the disc's own census: twelve coasters,
    /// eight track rides and four tour rides, and each theme's assets occupy one contiguous stretch of archive
    /// entries — Wonderland 19..74, Halloween 95..160, Lost Kingdom 170..250, Space 330..400. Sorting the census
    /// into those stretches gives exactly THREE coasters, TWO track rides and ONE tour ride per theme, four times
    /// over, which is not what a wrong grouping produces. So the port offers those, and says they are derived.</summary>
    public static class AttractionCatalog
    {
        static readonly int[][] Rides =
        {
            new[] { 220, 217, 216, 218, 227, 224, 222, 211, 210, 209, 221, 225, 226, 214 },
            new[] { 133, 129, 128, 131, 132, 140, 134, 122, 139, 137, 124, 121, 138, 136 },
            new[] { 40, 53, 39, 54, 49, 51, 52, 42, 55, 43, 45, 48, 50 },
            new[] { 377, 362, 369, 379, 371, 365, 376, 360, 364, 374, 375, 366, 370 },
        };
        static readonly int[][] Shops =
        {
            new[] { 237, 234, 236, 239, 235, 238, 232, 247 },
            new[] { 149, 146, 152, 151, 160, 150, 158, 147 },
            new[] { 65, 71, 62, 68, 60, 69, 64, 67 },
            new[] { 385, 381, 383, 387, 382, 386, 384, 390 },
        };
        /// <summary>Type 1, the roller coasters: three a theme (see the class note).</summary>
        static readonly int[][] Coasters =
        {
            new[] { 212, 213, 219 },
            new[] { 125, 126, 127 },
            new[] { 44, 46, 47 },
            new[] { 367, 368, 373 },
        };

        /// <summary>Type 6, the track rides: two a theme.</summary>
        static readonly int[][] TrackRides =
        {
            new[] { 215, 228 },
            new[] { 130, 143 },
            new[] { 41, 57 },
            new[] { 363, 378 },
        };

        /// <summary>Type 7, the tour rides: one a theme.</summary>
        static readonly int[][] TourRides =
        {
            new[] { 208 }, new[] { 142 }, new[] { 56 }, new[] { 372 },
        };

        static readonly int[][] Sideshows =
        {
            new[] { 248, 229, 244, 243, 245, 246 },
            new[] { 144, 159, 153, 154, 145, 155 },
            new[] { 66, 72, 70, 74, 58, 73 },
            new[] { 388, 393, 391, 380, 389, 394 },
        };

        /// <summary>Type 2, the features: toilets, the staff room, fountains, trees and the rest of the scenery
        /// (13, 14, 12, 13 per theme, the counts the theme table declares). The two sets hold the same features
        /// bar one of Space's, so this is set A.</summary>
        static readonly int[][] Features =
        {
            new[] { 197, 195, 185, 193, 179, 200, 201, 181, 180, 188, 173, 194, 191 },
            new[] { 109, 107, 105, 98, 106, 110, 95, 96, 99, 100, 101, 111, 103, 104 },
            new[] { 32, 31, 30, 19, 26, 29, 20, 21, 23, 24, 25, 28 },
            new[] { 353, 352, 350, 351, 343, 344, 334, 336, 337, 340, 341, 345, 346 },
        };

        /// <summary>A world's entries of one attraction type, in the theme table's own order; empty for a type
        /// whose list is scenario data (1 coasters, 6 track rides, 7 tour rides).</summary>
        public static IReadOnlyList<int> ForWorld(int world, int type)
        {
            if (world < 0 || world >= Rides.Length) return Array.Empty<int>();
            return type switch
            {
                3 => Rides[world],
                4 => Shops[world],
                5 => Sideshows[world],
                2 => Features[world],
                1 => Coasters[world],
                6 => TrackRides[world],
                7 => TourRides[world],
                _ => Array.Empty<int>(),
            };
        }

        public static IEnumerable<int> ForWorld(int world)
        {
            if (world < 0 || world >= Rides.Length) yield break;
            foreach (var e in Rides[world]) yield return e;
            foreach (var e in Shops[world]) yield return e;
            foreach (var e in Sideshows[world]) yield return e;
            foreach (var e in Features[world]) yield return e;
            foreach (var e in Coasters[world]) yield return e;
            foreach (var e in TrackRides[world]) yield return e;
            foreach (var e in TourRides[world]) yield return e;
        }
    }

    /// <summary>Putting an attraction on the map, as the game's placement check (0x80064558) and placement
    /// (0x80063B98) do it.</summary>
    public static class AttractionPlacement
    {
        /// <summary>The ghost's markers, common-sheet sprites (0x80064558): 0xA5 a footprint tile that is fine, 0xAA
        /// one on the attraction's front edge (turned to face out), 0xAF one that refuses; the entrance 0xA8 for
        /// rides and 0xAC for the rest, the exit 0xA9, both 0xAF when refused.</summary>
        public const int Body = 0xA5, Front = 0xAA, Refused = 0xAF, RideEntrance = 0xA8, OtherEntrance = 0xAC, ExitMarker = 0xA9;

        public readonly struct Marker
        {
            public readonly int X, Z, Sprite, Turns;
            public Marker(int x, int z, int sprite, int turns) { X = x; Z = z; Sprite = sprite; Turns = turns; }
        }

        static bool InMap(ParkMap m, int x, int z) => x >= 0 && x < m.Width - 1 && z >= 0 && z < m.Height - 1;

        /// <summary>Whether the tile outside a door may take the attraction's own path piece (every exit, and the
        /// entrance of anything that is not a ride). The game tests it as it tests the ride's queue piece and the
        /// footprint (0x80064558 → 0x8004D718), so path already there refuses the whole placement.
        ///
        /// ⚠ THE PORT'S DEPARTURE, master's call (the PC version does it): the path piece may land on path already
        /// there (path, or path and queue), and placing then joins that path to the door (<see cref="PathTool.LayDoors"/>
        /// lays path over path as the path tool does, linking it) instead of the blueprint going red. The no-build
        /// flags (0x01 no ground, 0x02 nothing may be built) still refuse; a ride's queue piece keeps the game's rule.</summary>
        static bool PathPieceAllows(MapTile t) =>
            ParkBuild.TileAllows(t) || (t.Raw0 is 2 or 13 && (t.Flags & (0x01 | 0x02)) == 0);

        /// <summary>The markers for an attraction with its footprint's corner at (ox, oz), and whether it may be placed.</summary>
        public static List<Marker> Ghost(ParkMap map, AttractionDefinition a, int ox, int oz, int rot, out bool placeable)
        {
            var list = new List<Marker>();
            placeable = true;
            var (w, d) = a.Footprint(rot);
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    int tx = ox + x, tz = oz + z;
                    if (!InMap(map, tx, tz) || !InMap(map, tx + 1, tz) || !InMap(map, tx, tz + 1)) { placeable = false; continue; }
                    if (!ParkBuild.CanBuild(map, tx, tz)) { placeable = false; list.Add(new Marker(tx, tz, Refused, 0)); continue; }
                    bool front = (rot & 3) switch { 1 => x == 0, 2 => z == d - 1, 3 => x == w - 1, _ => z == 0 };
                    list.Add(new Marker(tx, tz, front ? Front : Body, front ? rot & 3 : 0));
                }
            if (a.EntranceTile(ox, oz, rot) is { } e && InMap(map, e.X, e.Z))
            {
                bool ok = a.IsRide ? ParkBuild.TileAllows(map[e.X, e.Z]) : PathPieceAllows(map[e.X, e.Z]);
                placeable &= ok;
                list.Add(new Marker(e.X, e.Z, ok ? (a.IsRide ? RideEntrance : OtherEntrance) : Refused, a.EntranceTurn(rot)));
            }
            if (a.ExitTile(ox, oz, rot) is { } x2 && InMap(map, x2.X, x2.Z))
            {
                bool ok = PathPieceAllows(map[x2.X, x2.Z]);
                placeable &= ok;
                list.Add(new Marker(x2.X, x2.Z, ok ? ExitMarker : Refused, a.ExitTurn(rot)));
            }
            return list;
        }

        /// <summary>Place it (0x80063B98), in place on the map: each footprint tile becomes a footprint (type 5) and
        /// wears the record's ground pad, turned with it (the pad's own turns plus the rotation, where the game swaps
        /// 1 and 3); a pad tile with no sprite draws no ground (flags = 1). Then the entrance's own tile, on the
        /// footprint's edge, becomes 7 and the exit's 8, each facing out. What goes on the tiles outside them (a
        /// queue piece or path) is <see cref="PathTool.LayDoors"/>.</summary>
        public static void Place(ParkMap map, AttractionDefinition a, int ox, int oz, int rot)
        {
            rot &= 3;
            int padTurn = rot is 1 or 3 ? (rot + 2) & 3 : rot;
            for (int j = 0; j < a.Depth; j++)
                for (int i = 0; i < a.Width; i++)
                {
                    var (rx, rz) = a.Rotate(i, j, rot);
                    int tx = ox + rx, tz = oz + rz;
                    if (!InMap(map, tx, tz)) continue;
                    var t = map[tx, tz];
                    byte flags = t.Flags;
                    ushort ground = t.Ground;
                    int k = j * a.Width + i;
                    if (k < a.Pad.Length)
                    {
                        var (sprite, pf) = a.Pad[k];
                        if (sprite < 0) flags = 1;
                        else ground = (ushort)((ushort)sprite | (((pf & 3) + padTurn) & 3) << 12 | ((pf >> 3) & 1) << 15 | ((pf >> 2) & 1) << 14);
                    }
                    map.Tiles[tz * map.Width + tx] = new MapTile(5, t.Raw1, t.Links, t.Facing, ground, t.Shade, flags);
                }
            void Door((int X, int Z)? tile, byte type, int turn)
            {
                if (tile is not { } p || !InMap(map, p.X, p.Z)) return;
                var t = map[p.X, p.Z];
                // Facing, in the link bits' encoding (0x01 north, 0x04 east, 0x10 south, 0x40 west): the way out, away
                // from the attraction, toward the path a guest arrives on. The path code joins a path tile to an
                // entrance only when the entrance's facing bit points at it (0x8004E20C).
                byte facing = turn switch { 0 => 0x01, 1 => 0x40, 2 => 0x10, _ => 0x04 };
                map.Tiles[p.Z * map.Width + p.X] = new MapTile(type, t.Raw1, t.Links, facing, t.Ground, t.Shade, t.Flags);
            }
            Door(a.EntranceDoor(ox, oz, rot), 7, a.EntranceTurn(rot));
            Door(a.ExitDoor(ox, oz, rot), 8, a.ExitTurn(rot));
        }
    }
}
