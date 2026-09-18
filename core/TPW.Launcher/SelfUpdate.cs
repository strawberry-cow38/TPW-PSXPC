using System;
using System.Security.Cryptography;

namespace TPW.Launcher
{
    /// <summary>The checks that stand between a download and overwriting the running launcher.
    ///
    /// ⚠⚠ THE FAILURE THIS GUARDS IS BRICKING THE USER. A self-update writes over the only copy of the
    /// program the user has. If what arrives is a truncated download, a GitHub error page, a redirect body or
    /// a half-written file, and it is copied over the exe anyway, the launcher is gone and the user has no
    /// launcher with which to fix it. There is no recovery path from inside the thing that broke.
    ///
    /// So this refuses by default and accepts only on positive evidence -- and it lives in core, with tests,
    /// because it is the last check before an irreversible act and it is exactly the sort of code that never
    /// runs during development.</summary>
    public static class SelfUpdate
    {
        /// <summary>A self-contained Avalonia launcher is ~90 MB. Anything dramatically smaller is an error
        /// page or a partial transfer, not a program. Deliberately an order of magnitude below the real size
        /// rather than just under it: this is a sanity floor, not a size assertion, and a tight bound would
        /// reject a legitimately smaller future build.</summary>
        public const int MinPlausibleBytes = 10_000_000;

        /// <summary>Does this look like a Windows executable at all? PE files begin "MZ".
        ///
        /// ⚠ Cheap, and it catches the common disaster: an HTML error body starts "&lt;!DOCTYPE" or "&lt;html",
        /// never "MZ". A 404 page is a perfectly successful HTTP response, so nothing upstream of here will
        /// have complained about it.</summary>
        public static bool LooksLikeWindowsExe(byte[] bytes, int minBytes = MinPlausibleBytes) =>
            bytes != null && bytes.Length >= minBytes && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';

        public static string Sha256Of(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        /// <summary>Verdict on a downloaded update.</summary>
        public readonly struct Verdict
        {
            public readonly bool Accept;
            public readonly string Reason;
            public Verdict(bool a, string r) { Accept = a; Reason = r; }
        }

        /// <summary>Should these bytes be allowed to replace the running launcher?
        ///
        /// ⚠ AN ABSENT EXPECTED HASH IS NOT A PASS AND NOT A FAIL. If the publisher did not upload a hash we
        /// cannot verify identity, only plausibility -- so this accepts on the shape checks but SAYS the
        /// stronger check did not run. Silently treating "no hash published" as "hash verified" is how a
        /// verification step becomes decorative; the caller logs the reason either way, so a launcher that
        /// stops hash-checking says so in its own log rather than going quiet.</summary>
        public static Verdict Check(byte[] bytes, string expectedSha256)
        {
            if (bytes == null || bytes.Length == 0)
                return new Verdict(false, "download was empty");
            if (!LooksLikeWindowsExe(bytes))
                return new Verdict(false,
                    $"got {bytes.Length:n0} bytes and it is not a Windows executable "
                    + "(an error page or a partial transfer) — keeping the current launcher");

            if (string.IsNullOrWhiteSpace(expectedSha256))
                return new Verdict(true, "shape looks right; no published hash to verify against");

            string actual = Sha256Of(bytes);
            if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                return new Verdict(false,
                    $"hash mismatch — published {expectedSha256.Trim()[..16]}…, downloaded {actual[..16]}… "
                    + "— keeping the current launcher");

            return new Verdict(true, "hash matches the published build");
        }
    }
}
