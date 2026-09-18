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
        /// ⚠⚠ DELIBERATELY EMPTY. Nobody has hashed a real disc yet, so this project cannot honestly claim to
        /// recognise one -- and the consequence is that the launcher currently refuses every copy, which is
        /// the CORRECT behaviour rather than a gap to paper over. An entry invented to make the flow "work"
        /// would be a lie that only surfaces as wrong offsets producing plausible garbage, which is the most
        /// expensive kind of wrong this project has.
        ///
        /// To add one: hash the executable from a copy you own, and record the region and tick rate you
        /// MEASURED rather than the ones you expect.</summary>
        public static readonly IReadOnlyDictionary<string, GameVariant> Known =
            new Dictionary<string, GameVariant>(StringComparer.OrdinalIgnoreCase);

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
