using System;
using TPW.Data;
using TPW.Launcher;

// Runs the startup asset self-test against a disc image and prints the report, with no engine involved.
//
// ⭐ THIS EXISTS BECAUSE THE SELF-TEST'S OWN CORRECTNESS NEEDS PROVING ON REAL DATA. Unit tests cover it
// against synthetic bytes, which establishes that the logic is right; they cannot establish that the real
// disc is shaped the way the parser believes. Those are different claims and only this answers the second.
// It is also the thing to run when someone says "it won't start": same checks, same wording, no window.
//
// Exit code is the number of failed checks, so it is usable as a gate.
static class Program
{
    /// <summary>Print candidate hashes of the legal screen as the console would hold it: 320x256, 16bpp
    /// little-endian, row-major, no padding. Several candidates because three conventions are genuinely
    /// unknown, and printing all of them says WHICH one the hardware uses rather than just pass/fail.</summary>
    static void VramHash(DiscReader disc)
    {
        var f = disc.Find(AssetSelfTest.LegalScreen);
        if (f == null) { Console.WriteLine("no legal screen"); return; }
        if (!Tga.TryDecode(disc.ReadFile(f), out var img, out string err))
        { Console.WriteLine("decode failed: " + err); return; }

        Console.WriteLine($"{img.Width}x{img.Height} decoded");

        // ⭐ POSITION-INDEPENDENT INVARIANTS BISECT A HASH MISMATCH. A bare hash says "no" without saying
        // which assumption is wrong. These two split the problem: the peak channel triple is order-SENSITIVE
        // and position-INDEPENDENT, so it isolates channel order; the non-black count cannot change with row
        // order, so it isolates completeness. Together they say whether a mismatch is colour or layout.
        int nonBlack = 0, peakR = 0, peakG = 0, peakB = 0;
        for (int i = 0; i < img.Rgba.Length; i += 4)
        {
            int r = img.Rgba[i] >> 3, g = img.Rgba[i + 1] >> 3, b = img.Rgba[i + 2] >> 3;
            if ((r | g | b) != 0) nonBlack++;
            if (r > peakR) peakR = r;
            if (g > peakG) peakG = g;
            if (b > peakB) peakB = b;
        }
        Console.WriteLine($"  non-black pixels : {nonBlack:n0} of {img.Width * img.Height:n0}");
        Console.WriteLine($"  peak channel     : R={peakR} G={peakG} B={peakB}  (max 31)");

        // ⚠ THE PSX FRAMEBUFFER IS BGR, NOT RGB. A 16-bit VRAM word is 0bbbbbgggggrrrrr -- blue in the high
        // bits -- while a 15-bit TGA word is 0rrrrrgggggbbbbb. Same size, same layout, channels reversed. That
        // is invisible in every structural check: sizes match, the image decodes, the picture looks like a
        // picture. Only a byte-exact comparison against the hardware can see it.
        foreach (bool flip in new[] { false, true })
            foreach (bool bgr in new[] { false, true })
            foreach (int maskBit in new[] { 0, 1 })
            {
                var buf = new byte[img.Width * img.Height * 2];
                for (int y = 0; y < img.Height; y++)
                {
                    int srcRow = flip ? img.Height - 1 - y : y;
                    for (int x = 0; x < img.Width; x++)
                    {
                        int sp = (srcRow * img.Width + x) * 4;
                        // 8-bit back to 5-bit is exact: the decoder expands with (v<<3)|(v>>2), so >>3 inverts it.
                        int r = img.Rgba[sp] >> 3, g = img.Rgba[sp + 1] >> 3, b = img.Rgba[sp + 2] >> 3;
                        int w = bgr ? (maskBit << 15) | (b << 10) | (g << 5) | r
                                    : (maskBit << 15) | (r << 10) | (g << 5) | b;
                        int dp = (y * img.Width + x) * 2;
                        buf[dp] = (byte)(w & 0xFF);
                        buf[dp + 1] = (byte)(w >> 8);
                    }
                }
                using var sha = System.Security.Cryptography.SHA256.Create();
                string hex = Convert.ToHexString(sha.ComputeHash(buf)).ToLowerInvariant();
                Console.WriteLine($"  rows={(flip ? "bottom-up" : "top-down ")} {(bgr ? "BGR" : "RGB")} maskbit={maskBit}  {hex}");
            }
    }

    static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : null;
        if (path == null)
        {
            var probe = GameDataLocator.Probe();
            path = probe.SourcePath;
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("usage: tpwcheck <disc image>   (nothing found by probing)");
                return 1;
            }
            Console.WriteLine($"probed: {path}");
        }

        // --vramhash: emit the legal screen in the console's own framebuffer format so it can be compared
        // against a VRAM dump by hash alone -- neither side has to send an image anywhere.
        //
        // ⚠ THIS GOES THROUGH THE REAL DECODER ON PURPOSE. Re-implementing the conversion in a script to
        // "check the format" would test the script, which is the mistake that just shipped a broken launcher:
        // a harness that builds its own input is not testing the product.
        bool vramHash = Array.IndexOf(args, "--vramhash") >= 0;

        var id = GameDataLocator.Identify(path);
        Console.WriteLine($"identify: {id.Message}");
        Console.WriteLine($"variant : {id.Variant?.Id ?? "(unrecognised)"}");
        Console.WriteLine();

        DiscReader disc = null;
        try { disc = DiscReader.Open(path); }
        catch (Exception e) { Console.WriteLine("could not open: " + e.Message); return 1; }

        using (disc)
        {
            if (vramHash) { VramHash(disc); return 0; }

            var r = AssetSelfTest.Run(disc);
            Console.WriteLine(r.Summary());
            foreach (var c in r.Checks) Console.WriteLine("  " + c);
            Console.WriteLine();

            Console.WriteLine($"volume '{disc.VolumeId}', {disc.Files.Count} entries:");
            foreach (var f in disc.Files)
                Console.WriteLine($"  {f.Length,12:n0}  lba {f.Lba,-7} {f.Name}");

            return r.Failures;
        }
    }
}
