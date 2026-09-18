# Ride / shop definition record — schema

**The layout is ours. The values are EA's.** This documents the shape of a record
so an implementation can parse one; it deliberately carries **no real row**. See
[`records.README.md`](records.README.md) for why, and run
`findings/fable-scripts/recs.py` against your own copy to get the actual table.

197 records, 31 fields each.

## Header

| field | type | meaning |
|---|---|---|
| `folio` | int | which FOLIO file the record came from |
| `file` | str | that file's name, e.g. `0044.bin` |
| `rec_off` | int | byte offset of the record inside the file |
| `type` | int | record kind (ride / shop / other) |
| `text_id` | int | index into the localised string table — the displayed name |
| `fp_w`, `fp_h` | int | footprint in tiles |
| `entrance` | [int, int] | entrance tile offset within the footprint |
| `exit` | [int, int] | exit tile offset |
| `base_intensity` | int | the ride's intensity before per-level scaling |
| `body_len` | int | length of the variable part that follows |
| `b9`, `b11`, `b14`..`b17`, `w20` | int | **not yet identified**, named by offset |

## Per-level block, repeated

A ride has several upgrade levels; each carries its own economics.

| field | type | meaning |
|---|---|---|
| `cap_max` | int | guest capacity |
| `lifetime` | int | in **ticks** — see `ParkClock`, 99 ticks to a day |
| `cycles_max` | int | ride cycles before something (wear? servicing?) |
| `price` | int | ⚠ money is stored in **tenths of a pound**, so 600 is £60 |
| `b8_min`, `b8_max` | int | a range, purpose unconfirmed |
| `f28`, `f2C`, `f30`, `f40`, `f48`, `f4C` | int | **not yet identified**, named by offset |

## ⚠ Reading this honestly

**Eleven of the thirty-one fields are still just offsets with `b`/`f`/`w` names.**
That is the real state of it, and the naming makes it visible rather than
plausible-looking. A field called `f2C` cannot be mistaken for understood; a field
guessed as `comfort_rating` could be, and would then be inherited as fact by
whoever reads this next.

Two of fable's offsets in this area have already been corrected by live reads
(the object base by 8, the shop status field by 4), so treat the layout as good
and any individual offset as worth re-checking against your own copy before
anything depends on it.

## Synthetic example

**Not a real row** — hand-made, to show the shape only:

```json
{
  "folio": 0, "file": "0000.bin", "rec_off": 0,
  "type": 1, "text_id": 1,
  "fp_w": 2, "fp_h": 2,
  "entrance": [0, 0], "exit": [1, 0],
  "base_intensity": 50, "body_len": 64,
  "levels": [
    { "cap_max": 4, "lifetime": 99, "cycles_max": 10, "price": 600 }
  ],
  "name": "EXAMPLE — not from the game"
}
```
