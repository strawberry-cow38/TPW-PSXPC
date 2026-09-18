using System;
using System.Collections.Generic;

namespace TPW.Launcher
{
    public enum GameDataState
    {
        /// <summary>No file where we were told to look.</summary>
        Missing,
        /// <summary>A file is there and we do not recognise it. ⚠ NOT a synonym for "wrong" -- it may be a
        /// perfectly good disc of a region nobody has hashed yet. It IS a synonym for "do not proceed".</summary>
        Unrecognised,
        Known,
    }

    /// <summary>One release of the game, keyed by the hash of its executable.
    ///
    /// ⚠ THE VARIANT CARRIES THE TICK RATE, and that is not incidental. The sim clock runs at half the
    /// machine's frame rate, so a PAL disc (SLES, 50 Hz) ticks every 0.04 s and an NTSC one (SLUS, 60 Hz)
    /// every 0.0333 s. Same rule, different constant, and a port that hardcodes either one is silently wrong
    /// on half the discs in existence. Offsets are expected to differ between regions too, which is the other
    /// half of why identification has to happen before anything is read.</summary>
    public sealed class GameVariant
    {
        public string Id;            // e.g. "SLES-02688"
        public string Name;          // human-facing
        public string Region;        // PAL / NTSC-U / NTSC-J
        public double TickSeconds;   // 0.04 PAL, 0.0333... NTSC
    }

    /// <summary>What a known hash was taken OF. The same release is recognisable by several different files,
    /// and which one matched is worth telling the user -- "recognised your boot executable" and "recognised
    /// your disc image" mean different things about how portable that recognition is.</summary>
    public enum HashedThing
    {
        /// <summary>The boot executable, e.g. SLES_026.88. ⭐ PREFERRED: ~48 KB, and identical across every
        /// dump of the same disc, so sector mode, padding and iso-vs-bin all stop mattering.</summary>
        BootExecutable,
        /// <summary>A whole disc image. ⚠ Rip-sensitive -- a different dump of the same disc hashes
        /// differently. Accepted so copies that already work keep working, not because it is a good key.</summary>
        DiscImage,
        /// <summary>A data file from inside the disc.</summary>
        DataFile,
    }

    public sealed class KnownHash
    {
        public GameVariant Variant;
        public HashedThing What;
    }

    public readonly struct GameDataResult
    {
        public readonly GameDataState State;
        public readonly GameVariant Variant;   // null unless Known
        public readonly HashedThing What;      // meaningful only when Known
        public readonly string Message;        // shown to the user verbatim
        public GameDataResult(GameDataState s, GameVariant v, HashedThing w, string m)
        { State = s; Variant = v; What = w; Message = m; }
        public bool CanPlay => State == GameDataState.Known;
    }

    public static class GameData
    {
        /// <summary>Every hash that identifies a release, and what each hash was taken of.
        ///
        /// ⚠ SEVERAL HASHES MAP TO ONE VARIANT ON PURPOSE. A user may point us at an extracted boot
        /// executable, a whole disc image, or a data file, and all three are the same game. Keying on only one
        /// of them refuses copies we demonstrably recognise.
        ///
        /// ⭐ PREFER THE BOOT EXECUTABLE. It is ~48 KB, free to hash, and identical across every dump of the
        /// same disc -- sector mode, padding and iso-vs-bin all fall away. The disc-image hash below is
        /// rip-sensitive and is kept only so a copy that works today does not stop working (tinyclaw's point,
        /// and the right one: migrating a key should never strand the person already using it).
        ///
        /// ⚠ NAME COLLISION WORTH KNOWING: the image on the build box is called `TPW.bin` (515,998,224 bytes,
        /// a raw 2352-byte-sector disc image) and there is ALSO a `TPW.BIN` data file INSIDE that disc
        /// (1,065,308 bytes). Same name, four hundred times the size, completely different things. Anything
        /// that resolves one by filename alone will eventually pick the wrong one.
        ///
        /// Every entry here was hashed from a real copy. Adding one means hashing your own and recording the
        /// region and tick rate you MEASURED, not the ones you expect.</summary>
        static readonly GameVariant PalSles02688 = new()
        {
            Id = "SLES-026.88",
            Name = "Theme Park World (PSX)",
            Region = "PAL",
            TickSeconds = 0.04,   // 50 Hz half-rate; see ParkClock
        };

        public static readonly IReadOnlyDictionary<string, KnownHash> Known =
            new Dictionary<string, KnownHash>(StringComparer.OrdinalIgnoreCase)
            {
                // ⭐ the stable one
                ["e5cee3b51a26ee3a6965cf20f4394f1c9bb9d741"] =
                    new KnownHash { Variant = PalSles02688, What = HashedThing.BootExecutable },
                // a data file from the same disc
                ["f7dadde48db4b9f610e8757aaf64279e26e587c3"] =
                    new KnownHash { Variant = PalSles02688, What = HashedThing.DataFile },
                // ⚠ rip-sensitive, kept so the copy already in use keeps working
                ["2167C58486F14183E393F2010D33ABBDF958953D"] =
                    new KnownHash { Variant = PalSles02688, What = HashedThing.DiscImage },
            };

        /// <summary>Identify a copy of the game from the hash of its executable.
        ///
        /// ⚠ REFUSES RATHER THAN GUESSES, which is the whole design. An unrecognised build read with another
        /// region's offsets does not fail loudly -- it produces numbers that look entirely reasonable and are
        /// wrong, and the error surfaces hours later somewhere unrelated. ScummVM has refused unknown
        /// variants for twenty years for exactly this reason. "Best effort" is the wrong instinct here.</summary>
        public static GameDataResult Identify(string sha1, IReadOnlyDictionary<string, KnownHash> known = null)
        {
            known ??= Known;

            if (string.IsNullOrWhiteSpace(sha1))
                return new GameDataResult(GameDataState.Missing, null, default,
                    "No game data found. Point the launcher at your own copy of Theme Park World.");

            if (known.TryGetValue(sha1.Trim(), out var k) && k?.Variant != null)
            {
                // ⚠ Say what matched, not just that something did. A user told "recognised your disc image"
                // learns their recognition is rip-specific; told only "found it", they learn nothing and are
                // surprised later when a re-dump stops working.
                string what = k.What switch
                {
                    HashedThing.BootExecutable => "boot executable",
                    HashedThing.DiscImage => "disc image",
                    _ => "data file",
                };
                return new GameDataResult(GameDataState.Known, k.Variant, k.What,
                    $"Found {k.Variant.Name} ({k.Variant.Region}) -- matched your {what}.");
            }

            // ⚠ NAME THE HASH. "Unrecognised build" with no hash is unactionable -- the user cannot tell us
            // what they have, and we cannot add it. The hash is the one piece of information that turns a
            // dead end into a bug report someone can act on.
            return new GameDataResult(GameDataState.Unrecognised, null, default,
                $"Unrecognised build (sha1 {sha1.Trim()}). This copy is not one the port knows how to read, "
                + "so it will not run rather than guess at the layout. Please report the hash.");
        }
    }
}
