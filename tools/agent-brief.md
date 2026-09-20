# House conventions for an agent working on this port

Hand this file to any agent (astra, fable, whoever) before the task. It is short on purpose: every
line below has earned its place by preventing a specific failure, and the "source disagreements" rule
has been worth more than the code twice over.

⚠ **This lived in `scratchpad/conventions.md` and was lost** — the scratchpad is tmpfs and a reboot
ate it. It lives in the repo now. If you are writing an agent brief, point it here.

## What this project is

A 1:1 reimplementation of Theme Park World (PSX, PAL, SLES-026.88) in C#. The disc is at
`/home/ec2-user/tpw/tpw_psx.iso`; the extracted executable is `/home/ec2-user/tpw/ext/TPW.BIN`,
loaded at `0x80010000`. `findings/` holds the analysis reports the code is written from.

Disassembly scripts are in `findings/fable-scripts/`: `fn.py ADDR` prints a whole function and is
alignment-safe, `callers.py ADDR` finds callers, `fieldx.py OFF` finds who touches a struct offset,
`xref.py ADDR` finds references. Prefer `fn.py`; `mipsdis.py`'s raw output can desync.

## The rules

1. **Never invent a constant.** Every number in the code comes from an address you can quote, or it
   is marked GUESS with a confidence and what would settle it. A plausible number nobody can trace is
   worse than an honest gap.

2. **Carry READ / GUESS into the code, not just the report.** A doc comment that says where a value
   came from is how the next person knows whether they may change it.

3. **Reproduce oddities. Mark them `⚠ DO NOT FIX`.** Off-by-ones, sticky flags, a loop that does not
   end where you would end it — those are the port. Tidying one is a bug with a good motive.

4. ⭐ **Where the binary disagrees with an existing findings file, KEEP THE FINDINGS FILE'S VERSION
   in the code and write the disagreement down** in a `§0 SOURCE DISAGREEMENTS` table with addresses
   and both readings. Do not silently substitute your own. This is the single most valuable thing an
   agent does here: three runs have produced disagreements that were right, and each was worth more
   than the code around it — because a silent substitution looks exactly like working code.

5. **Every test states what it REJECTS**, in a comment above it. "Asserts the right answer" is not a
   test; "rejects Chebyshev distance, subtract-before-shift, and the inclusive boundary" is.

6. **Mutation-test it.** Write a runner like `tools/mutate_statistics.py`, produce a JSON audit, and
   require every mutation to be caught. A survivor means the test is weak, the rule is duplicated, or
   the fixture is degenerate — fix it and record what it was.

7. **Measure with a control.** A number on its own is not evidence. "It works" needs the run where it
   should NOT work, and a check whose pass looks like its failure is not a check.

8. **Say what you did not establish.** The gaps are as useful as the findings, and an agent that
   hides one costs the next person a day finding it the hard way.

## Practical

- Work in the worktree you were given, on its branch. Do not touch `game/` unless asked.
- `dotnet test tests/TPW.Sim.Tests/` must pass before you finish.
- ⚠ `dotnet build` at the repo root does **not** build `game/TPWGodot.csproj` and still says "Build
  succeeded". Build `game/` explicitly if you touch it.
- Report at the end: how many tests, how many mutations, and the source disagreements you found.
