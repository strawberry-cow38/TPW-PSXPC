#!/usr/bin/env python3
"""READ: extract ride model lengths from SLES-026.88; findings/animation-phases.md.

Reads the archive directly, never writes beside the disc. No 88-byte descriptor
is stored there: 0x8002C5FC..604 copies mesh+0 to descriptor+0x38 at load time.
The Markdown table is an explicitly conditional timing calculation, not a live
measurement. Usage: python3 tools/ride_phases.py /path/FOLIO.GAZ --markdown
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path


# READ: archive directory 0x800BBFA0; container accessors 0x8003084C/308C4.
DIRECTORY_START = 8
DIRECTORY_STRIDE = 8
CONTAINER_MAGIC = 0x96
CONTAINER_HEADER = 0x20
SUBENTRY_STRIDE = 8
MESH_HEADER = 0x48
# READ: 0x8006A76C and folio.md/rides.md identify the English string entry.
ENGLISH_ENTRY = 0x197
RIDE_TYPES = (1, 3, 6, 7)
FLAT_RIDE = 3
# READ: 0x8009F494/4B8, 0x8009C524..534. Three data blocks (rides.md §1.3).
LEVELS = 3
LEVEL_STRIDE = 0x34
CYCLES_MIN_OFFSET = 0x40
CYCLES_MAX_OFFSET = 0x44
# READ: 0x80065914/920/94C, 0x800BC2E0. Rate MEASURED in arrivals.md §2.1.
FIXED_SHIFT = 12
DELTA_CAP = 0x4000
REFERENCE_DELTA = 9984
TICKS_PER_SECOND = 25


def u32(data, offset):
    return struct.unpack_from('<I', data, offset)[0]


def archive_entries(data):
    """READ: pairs start at +8, after both the count and the 0x17 word."""
    count = u32(data, 0)
    if DIRECTORY_START + DIRECTORY_STRIDE * count > len(data):
        raise ValueError('archive directory exceeds file')
    result = []
    for index in range(count):
        offset, size = struct.unpack_from('<II', data,
                                         DIRECTORY_START + DIRECTORY_STRIDE * index)
        if offset + size > len(data):
            raise ValueError(f'entry {index} exceeds archive')
        result.append((offset, data[offset:offset + size]))
    return result


def model_lengths(data):
    """READ: one descriptor per sub-entry, not per logical phase-map slot.

    All 83 ride containers are raw. Reject packed sub-entries here instead of
    interpreting compressed bytes as a mesh; the game's expansion is 0x800BFD9C.
    """
    count = u32(data, 4)
    table_count = u32(data, 0x1C)
    mapping = list(struct.unpack_from(f'<{table_count}i', data, CONTAINER_HEADER))
    directory = CONTAINER_HEADER + 4 * table_count
    if count == 0 or directory + count * SUBENTRY_STRIDE > len(data):
        raise ValueError('missing or truncated sub-entry directory')
    lengths, offsets = [], []
    for index in range(count):
        offset, packed = struct.unpack_from('<II', data, directory + index * SUBENTRY_STRIDE)
        if packed:
            raise ValueError('compressed mesh: expansion required before reading length')
        if offset + MESH_HEADER > len(data):
            raise ValueError('truncated mesh header')
        # ⭐ READ: lhu mesh+0, sh descriptor+0x38 (0x8002C5FC..604).
        lengths.append(struct.unpack_from('<H', data, offset)[0])
        offsets.append(offset)
    return mapping, lengths, offsets


def phase_length(mapping, lengths, logical_phase):
    """READ: 0x80030364 → 0x80065DC0..C8 → 0x800300E4.

    Logical phase outside the map gives -1; a negative mapped index falls back
    to zero. Only the resulting sub-entry index wraps modulo the mesh count.
    """
    sub = mapping[logical_phase] if 0 <= logical_phase < len(mapping) else -1
    if sub < 0:
        sub = 0
    return lengths[sub % len(lengths)]


def phase_ticks(length, delta=REFERENCE_DELTA):
    """READ-derived: calls to complete from zero at a constant positive delta.

    ⚠ DO NOT FIX the length-1 or carry overshoot into the next phase: the
    original resets the accumulator to zero (0x80065970). Even length 1 needs
    a call. This helper is a table calculation, not the full signed clock port.
    """
    if length < 1 or delta <= 0:
        raise ValueError('positive length and delta required for this calculation')
    delta = min(delta, DELTA_CAP)
    target = (length - 1) << FIXED_SHIFT
    return max(1, (target + delta - 1) // delta)


def ride_rows(data):
    entries = archive_entries(data)
    strings = entries[ENGLISH_ENTRY][1]
    rows = []
    for index, (archive_offset, entry) in enumerate(entries):
        if len(entry) < CONTAINER_HEADER or u32(entry, 0) != CONTAINER_MAGIC:
            continue
        record = u32(entry, 0x14)
        if record == 0:
            continue
        kind = u32(entry, record)
        if kind not in RIDE_TYPES:
            continue
        mapping, lengths, offsets = model_lengths(entry)
        text_id = u32(entry, record + 4)
        text_offset = u32(strings, 4 + 4 * text_id)
        name = strings[text_offset:strings.index(b'\0', text_offset)].decode('latin1').strip()
        minima = [u32(entry, record + CYCLES_MIN_OFFSET + LEVEL_STRIDE * level)
                  for level in range(LEVELS)]
        maxima = [u32(entry, record + CYCLES_MAX_OFFSET + LEVEL_STRIDE * level)
                  for level in range(LEVELS)]
        # ⚠ DO NOT FIX: this does NOT clamp the result to the record's minimum.
        # Bounce on Iggy starts below its listed minimum (0x8009C52C..534).
        defaults = [max(1, maximum // 2) for maximum in maxima]
        length = phase_length(mapping, lengths, 1)
        ticks = phase_ticks(length) if kind == FLAT_RIDE else None
        rows.append(dict(folio=index, name=name, type=kind, archive_offset=archive_offset,
                         record_offset=record, phase_map=mapping, mesh_offsets=offsets,
                         mesh_lengths=lengths, logical_phase_1_length=length,
                         cycles_min=minima, cycles_max=maxima, default_cycles=defaults,
                         reference_phase_ticks=ticks,
                         reference_run_ticks=([ticks * c for c in defaults]
                                              if ticks is not None else None)))
    return rows


def markdown(rows):
    lines = [
        '# Ride phase lengths — PAL SLES-026.88', '',
        'Generated by `tools/ride_phases.py`; interpretation and addresses in',
        '[animation-phases.md](animation-phases.md). Lengths, names, offsets and slider',
        'values are **READ** from FOLIO.GAZ. Times are **READ-derived, conditional**:',
        '`P = max(1, ceil(((L-1)<<12)/9984))` calls, at a constant normal-speed delta',
        'of 9984 (the common measured value in arrivals.md §2.1), 25 calls/second.',
        'Run seconds = `C × P / 25`. This is a reproducible reference schedule, **not',
        'a measurement of each ride**. The exact run uses the actual deltas, including',
        'its first call after loading; stalls, half speed and IRQ timing change it.', '',
        'All 59 flat rides have map `[-1, 0, -1, -1]`, one raw mesh at entry+0x38.',
        'The archive address below holds the length halfword itself. `C` and run times',
        'are L0/L1/L2. Roster presence does not establish scenario availability.', '',
        '| FOLIO | Name | Length address in GAZ | L | P (calls) | C L0/L1/L2 | Run seconds L0/L1/L2 |',
        '|---|---|---|---:|---:|---|---|',
    ]
    for row in rows:
        if row['type'] != FLAT_RIDE:
            continue
        address = row['archive_offset'] + row['mesh_offsets'][0]
        cycles = '/'.join(map(str, row['default_cycles']))
        seconds = '/'.join(f'{t / TICKS_PER_SECOND:.2f}' for t in row['reference_run_ticks'])
        lines.append(f"| 0x{row['folio']:03X} | {row['name']} | 0x{address:X} | "
                     f"{row['logical_phase_1_length']} | {row['reference_phase_ticks']} | "
                     f'{cycles} | {seconds} |')
    lines += ['', '## Other ride classes: model lengths are not run durations', '',
              '**READ:** status-2 slot 71 is overridden for types 1, 6 and 7;',
              'see animation-phases.md §4. Lists are in sub-entry order. All meshes',
              'are raw; JSON output provides each individual entry-relative offset.',
              'Trip/run time: **NOT ESTABLISHED by this table**.', '',
              '| FOLIO | Name | Type | GAZ entry base | Logical phase map | Mesh lengths |',
              '|---|---|---:|---|---|---|']
    for row in rows:
        if row['type'] == FLAT_RIDE:
            continue
        lines.append(f"| 0x{row['folio']:03X} | {row['name']} | {row['type']} | "
                     f"0x{row['archive_offset']:X} | {row['phase_map']} | {row['mesh_lengths']} |")
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--markdown', action='store_true')
    args = parser.parse_args()
    data = args.archive.read_bytes()
    rows = ride_rows(data)
    if args.markdown:
        print(markdown(rows), end='')
    else:
        print(json.dumps(dict(archive_sha256=hashlib.sha256(data).hexdigest(),
                              reference_delta=REFERENCE_DELTA, rides=rows), indent=2))


if __name__ == '__main__':
    main()
