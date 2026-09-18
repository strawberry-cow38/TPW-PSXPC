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

    public readonly struct GameDataResult
    {
        public readonly GameDataState State;
        public readonly GameVariant Variant;   // null unless Known
        public readonly string Message;        // shown to the user verbatim
        public GameDataResult(GameDataState s, GameVariant v, string m) { State = s; Variant = v; Message = m; }
        public bool CanPlay => State == GameDataState.Known;
    }

    public static class GameData
    {
        /// <summary>Known releases by executable hash.
        ///
        /// ⚠ ONE ENTRY, AND EVERY FIELD IN IT WAS MEASURED. Added 2026-09-18 from a copy on the build box.
        /// The boot id `SLES_026.88` was read out of the image itself (scanned for the boot identifier, not
        /// inferred from a filename), which makes it the PAL/European release -- and that matches the source
        /// tinyclaw's behaviour analysis cites, independently. SLES is a 50 Hz part, and the sim clock is
        /// measured at half the frame rate, so TickSeconds is 0.04.
        ///
        /// ⚠⚠ THIS KEYS ON A DISC IMAGE, WHICH IS RIP-SENSITIVE. The hash is of a raw 2352-byte-per-sector
        /// .bin (515,998,224 bytes = exactly 219,387 sectors). A different dump of the SAME disc -- different
        /// sector mode, different padding, a .iso rather than a .bin -- hashes differently and will be
        /// reported Unrecognised even though the game is identical. That is the safe direction to fail, but it
        /// is a real limitation: the better long-term key is the extracted EXECUTABLE, which is stable across
        /// rips, and this should move to that once the port can read files out of the image. Recorded here
        /// rather than discovered by the first person whose .iso is rejected.
        ///
        /// To add another: hash a copy you own, read its boot id out of the image, and record the region and
        /// tick rate you MEASURED rather than the ones you expect.</summary>
        public static readonly IReadOnlyDictionary<string, GameVariant> Known =
            new Dictionary<string, GameVariant>(StringComparer.OrdinalIgnoreCase)
            {
                ["2167C58486F14183E393F2010D33ABBDF958953D"] = new GameVariant
                {
                    Id = "SLES-026.88",
                    Name = "Theme Park World (PSX, disc image)",
                    Region = "PAL",
                    TickSeconds = 0.04,   // 50 Hz half-rate; see ParkClock
                },
            };

        /// <summary>Identify a copy of the game from the hash of its executable.
        ///
        /// ⚠ REFUSES RATHER THAN GUESSES, which is the whole design. An unrecognised build read with another
        /// region's offsets does not fail loudly -- it produces numbers that look entirely reasonable and are
        /// wrong, and the error surfaces hours later somewhere unrelated. ScummVM has refused unknown
        /// variants for twenty years for exactly this reason. "Best effort" is the wrong instinct here.</summary>
        public static GameDataResult Identify(string sha1, IReadOnlyDictionary<string, GameVariant> known = null)
        {
            known ??= Known;

            if (string.IsNullOrWhiteSpace(sha1))
                return new GameDataResult(GameDataState.Missing, null,
                    "No game data found. Point the launcher at your own copy of Theme Park World.");

            if (known.TryGetValue(sha1.Trim(), out var v) && v != null)
                return new GameDataResult(GameDataState.Known, v, $"Found {v.Name} ({v.Region}).");

            // ⚠ NAME THE HASH. "Unrecognised build" with no hash is unactionable -- the user cannot tell us
            // what they have, and we cannot add it. The hash is the one piece of information that turns a
            // dead end into a bug report someone can act on.
            return new GameDataResult(GameDataState.Unrecognised, null,
                $"Unrecognised build (sha1 {sha1.Trim()}). This copy is not one the port knows how to read, "
                + "so it will not run rather than guess at the layout. Please report the hash.");
        }
    }
}
