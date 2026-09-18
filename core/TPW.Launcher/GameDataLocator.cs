using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace TPW.Launcher
{
    /// <summary>Finds a copy of the game on disk and identifies it. The half of the check that touches a
    /// filesystem, kept apart from the pure rules in <see cref="GameData"/> so those stay testable without one.
    ///
    /// ⚠ The port needs the DATA, not the executable, so what a user points us at is whatever they happen to
    /// have: a raw disc image, a folder of files extracted from one, or a single boot executable. All three
    /// are legitimate and all three are hashable, which is why the variant table carries several hashes per
    /// release rather than insisting on one shape.</summary>
    public static class GameDataLocator
    {
        /// <summary>Where to look when the user has not chosen. ⚠ The first entry is where strawberry's copy
        /// actually is -- a real path from a real machine, not a guess at a convention.</summary>
        public static readonly string[] DefaultProbes =
        {
            @"C:\TPW.bin",
            @"C:\Games\TPW.bin",
            @"C:\Program Files (x86)\Theme Park World",
        };

        public static string Sha1Of(string path)
        {
            using var sha = SHA1.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        /// <summary>Identify whatever is at <paramref name="path"/>: a file is hashed directly; a directory is
        /// searched for something recognisable.
        ///
        /// ⚠ A DIRECTORY IS TRIED AGAINST EVERY CANDIDATE, SMALLEST FIRST, and that order is deliberate. A
        /// boot executable is ~48 KB and a disc image is ~500 MB; hashing the small ones first means the common
        /// case costs milliseconds, and it also means the STABLE key wins when a folder holds both an extracted
        /// executable and the image it came from. Hashing in directory order would let a rip-sensitive match
        /// shadow a rip-proof one purely by filename luck.</summary>
        public static GameDataResult Identify(string path, IReadOnlyDictionary<string, KnownHash> known = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return GameData.Identify(null, known);

            try
            {
                if (File.Exists(path)) return GameData.Identify(Sha1Of(path), known);

                if (Directory.Exists(path))
                {
                    var files = new List<FileInfo>();
                    foreach (var f in new DirectoryInfo(path).GetFiles("*", SearchOption.TopDirectoryOnly))
                        files.Add(f);
                    files.Sort((a, b) => a.Length.CompareTo(b.Length));   // smallest first: cheap, and prefers the stable key

                    GameDataResult firstSeen = default; bool any = false;
                    foreach (var f in files)
                    {
                        var r = GameData.Identify(Sha1Of(f.FullName), known);
                        if (r.CanPlay) return r;
                        if (!any) { firstSeen = r; any = true; }
                    }
                    // Nothing recognised. Report the folder rather than one arbitrary file's hash, or the user
                    // chases a hash for a file that was never the point.
                    return new GameDataResult(GameDataState.Unrecognised, null, default,
                        $"Nothing recognisable in {path} ({files.Count} file(s) checked). "
                        + "Point the launcher at your Theme Park World disc image, or at a folder extracted from one.");
                }
            }
            catch (Exception e)
            {
                // ⚠ An unreadable file is NOT an unrecognised game. Saying "unrecognised" for a permissions
                // error sends the user hunting for the wrong problem entirely.
                return new GameDataResult(GameDataState.Missing, null, default,
                    $"Could not read {path}: {e.Message}");
            }

            return new GameDataResult(GameDataState.Missing, null, default,
                $"Nothing at {path}. Point the launcher at your own copy of Theme Park World.");
        }

        /// <summary>Try the default locations in order and return the first that identifies. Used on startup so
        /// a user whose copy is already in an obvious place never has to pick.</summary>
        public static GameDataResult Probe(IReadOnlyDictionary<string, KnownHash> known = null)
        {
            foreach (var p in DefaultProbes)
            {
                var r = Identify(p, known);
                if (r.CanPlay) return r;
            }
            return new GameDataResult(GameDataState.Missing, null, default,
                "No game data found. Use Locate… to point the launcher at your copy of Theme Park World.");
        }
    }
}
