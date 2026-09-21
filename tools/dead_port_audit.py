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

# ⚠⚠ A TYPE IS NOT AN ENTRY POINT, AND CONFLATING THEM INFLATED THIS NUMBER FOR MONTHS.
# `public readonly record struct ObjectiveState(uint ParkBits, uint BonusBits)` matches DECL —
# it is a name followed by a parenthesis — so the audit then asked "does game/ contain
# `ObjectiveState(`?", i.e. "is this type ever CONSTRUCTED there". A type the host receives,
# passes and reads without ever constructing therefore looked exactly like dead behaviour.
# ObjectiveState, ObjectiveAward, ObjectiveDescription, ObjectiveMinigameResult, RideSliderRange
# and RideSliderSave were all on the list while the methods around them were wired and running.
# Types are still reported, separately and last, because "nothing constructs it" is occasionally
# worth a look — but they are not counted in the headline.
# ⚠ THE KEYWORDS STACK: "public readonly record struct Foo". A pattern that stops at the first
# keyword captures "struct" as the type name and finds nothing. Consume the whole run.
TYPEDECL = re.compile(r"\b(?:class|struct|interface|enum|record)(?:\s+(?:class|struct|record))*\s+(\w+)")


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

    type_names = set()
    for f in sim_files:
        txt = open(f, encoding="utf-8", errors="ignore").read()
        type_names.update(m.group(1) for m in TYPEDECL.finditer(txt))

    found = collections.defaultdict(list)
    types = collections.defaultdict(list)
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
                bucket = types if name in type_names else found
                if name not in [row[0] for row in bucket[cls]]:
                    # For a type, "named" means named at all in game/, not constructed there.
                    mentions = len(re.findall(r"\b" + re.escape(name) + r"\b", game)) \
                               if name in type_names else 0
                    bucket[cls].append((name, len(re.findall(pat, tests)), mentions))

    def show(groups, heading):
        for cls in sorted(groups, key=lambda c: (-len(groups[c]), c)):
            names = sorted(groups[cls])
            print(f"  {cls}")
            for row in (names if show_all else names[:8]):
                name, t, mentions = row
                extra = f", named {mentions}x in game/" if mentions else ""
                print(f"      {name:<32} {t:>3} test refs{extra}")
            if not show_all and len(names) > 8:
                print(f"      ... and {len(names) - 8} more (--all)")

    total = sum(len(v) for v in found.values())
    print(f"sim entry points the game never names: {total} candidates in {len(found)} files\n")
    show(found, "methods")

    tt = sum(len(v) for v in types.values())
    live = sum(1 for v in types.values() for _, _, m in v if m)
    print(f"\n  ── and {tt} TYPES the game never CONSTRUCTS, {live} of which it does name "
          f"(not counted above; see the note in this file) ──")
    show(types, "types")
    return 0


if __name__ == "__main__":
    sys.exit(main())
