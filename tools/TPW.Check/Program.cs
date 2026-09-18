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

        var id = GameDataLocator.Identify(path);
        Console.WriteLine($"identify: {id.Message}");
        Console.WriteLine($"variant : {id.Variant?.Id ?? "(unrecognised)"}");
        Console.WriteLine();

        DiscReader disc = null;
        try { disc = DiscReader.Open(path); }
        catch (Exception e) { Console.WriteLine("could not open: " + e.Message); return 1; }

        using (disc)
        {
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
