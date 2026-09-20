#!/usr/bin/env python3
"""Find sim behaviour the running game never calls.

    python3 tools/dead_port_audit.py [--all]

⭐ THIS PORT'S DOMINANT BUG IS CODE THAT IS CORRECT, TESTED AND NEVER REACHED. In one day:
the needs clock was ported and never called (every guest kept its spawn hunger for its whole
visit, which made shops look broken); the idle pass was ported and never called (its first act
is the go-home check, so NOBODY EVER LEFT and the park filled with maximally miserable people);
and EjectEveryone was an empty body that swallowed every guest aboard a ride that broke down.
None of them fail a build, a test or a run. They are invisible by construction.

So this looks for the shape rather than waiting for the next one to bite: a public sim entry
point that the TESTS exercise, the sim does not call internally, and `game/` never names.

⚠ CANDIDATES, NOT VERDICTS. It matches on identifier, so it cannot see a call made through an
interface, a delegate or a vtable-style indirection, and it will list value-type helpers that
only tests have reason to name. Confirm each before acting -- but a name on this list has earned
a look, because everything on it is unreachable as far as a plain reader can tell.
"""
import glob, os, re, sys, collections

SKIP = {"get", "set", "if", "for", "while", "switch", "return", "new", "throw", "lock", "using", "catch"}
DECL = re.compile(r"public\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\s\?]+?\s(\w+)\s*\(")


def read(pats):
    out = []
    for p in pats:
        for f in glob.glob(p, recursive=True):
            out.append(open(f, encoding="utf-8", errors="ignore").read())
    return "\n".join(out)


def main():
    show_all = "--all" in sys.argv
    sim_files = sorted(glob.glob("core/TPW.Sim/*.cs"))
    game = read(["game/*.cs"])
    tests = read(["tests/**/*.cs"])
    sim = read(["core/TPW.Sim/*.cs"])

    found = collections.defaultdict(list)
    for f in sim_files:
        cls = os.path.basename(f)[:-3]
        txt = open(f, encoding="utf-8", errors="ignore").read()
        for m in DECL.finditer(txt):
            name = m.group(1)
            if name in SKIP or name == cls or name.startswith("_"):
                continue
            pat = r"\b" + re.escape(name) + r"\s*\("
            if len(re.findall(pat, game)) == 0 \
               and len(re.findall(pat, sim)) <= 1 \
               and len(re.findall(pat, tests)) > 0:
                if name not in [n for n, _ in found[cls]]:
                    found[cls].append((name, len(re.findall(pat, tests))))

    total = sum(len(v) for v in found.values())
    print(f"sim entry points the game never names: {total} candidates in {len(found)} files\n")
    for cls in sorted(found, key=lambda c: (-len(found[c]), c)):
        names = sorted(found[cls])
        print(f"  {cls}")
        for name, t in (names if show_all else names[:8]):
            print(f"      {name:<32} {t:>3} test refs")
        if not show_all and len(names) > 8:
            print(f"      ... and {len(names) - 8} more (--all)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
