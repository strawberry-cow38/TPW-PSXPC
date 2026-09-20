#!/usr/bin/env python3
"""Break extractor rules one at a time, require a failing test, always restore.

Usage: python3 -B tools/mutate_ride_phases.py /path/FOLIO.GAZ
Only tools/ride_phases.py in this worktree is temporarily changed. No game file
is written. Tests require the disc, so an unavailable fixture is not a kill.
"""
import os
import subprocess
import sys
from pathlib import Path


MUTATIONS = [
    ('archive directory +4', 'DIRECTORY_START = 8', 'DIRECTORY_START = 4'),
    ('archive pair stride 4', 'DIRECTORY_STRIDE = 8', 'DIRECTORY_STRIDE = 4'),
    ('skip archive bounds', "if offset + size > len(data):", 'if False:'),
    ('mesh count from unpacked size', 'count = u32(data, 4)', 'count = u32(data, 0x34)'),
    ('phase-map count from mesh count', 'table_count = u32(data, 0x1C)', 'table_count = u32(data, 4)'),
    ('descriptor stride on disc', 'SUBENTRY_STRIDE = 8', 'SUBENTRY_STRIDE = 88'),
    ('no phase-map skip', 'directory = CONTAINER_HEADER + 4 * table_count', 'directory = CONTAINER_HEADER'),
    ('length from bounds', "struct.unpack_from('<H', data, offset)[0]", "struct.unpack_from('<H', data, offset + 0x38)[0]"),
    ('signed length', "struct.unpack_from('<H', data, offset)[0]", "struct.unpack_from('<h', data, offset)[0]"),
    ('word length', "struct.unpack_from('<H', data, offset)[0]", "struct.unpack_from('<I', data, offset)[0]"),
    ('skip decompression flag', 'if packed:', 'if False:'),
    ('skip mesh-header bounds', 'if offset + MESH_HEADER > len(data):', 'if False:'),
    ('unsigned map', "f'<{table_count}i'", "f'<{table_count}I'"),
    ('logical phase directly indexes mesh', 'sub = mapping[logical_phase] if 0 <= logical_phase < len(mapping) else -1', 'sub = logical_phase'),
    ('allow negative logical indexing', '0 <= logical_phase < len(mapping)', 'logical_phase < len(mapping)'),
    ('remove negative mapped fallback', 'if sub < 0:', 'if False:'),
    ('wrap by logical-slot count', 'sub % len(lengths)', 'sub % len(mapping)'),
    ('remove subentry modulo', 'lengths[sub % len(lengths)]', 'lengths[sub]'),
    ('wrong level stride', 'LEVEL_STRIDE = 0x34', 'LEVEL_STRIDE = 0x30'),
    ('minimum from maximum', 'CYCLES_MIN_OFFSET = 0x40', 'CYCLES_MIN_OFFSET = 0x44'),
    ('maximum from minimum', 'CYCLES_MAX_OFFSET = 0x44', 'CYCLES_MAX_OFFSET = 0x40'),
    ('round default up', 'maximum // 2', '(maximum + 1) // 2'),
    ('remove default floor', 'max(1, maximum // 2)', 'maximum // 2'),
    ('clamp default to minimum', 'defaults = [max(1, maximum // 2) for maximum in maxima]', 'defaults = [max(minimum, maximum // 2) for minimum, maximum in zip(minima, maxima)]'),
    ('no length decrement', '(length - 1) << FIXED_SHIFT', 'length << FIXED_SHIFT'),
    ('wrong fixed scale', 'FIXED_SHIFT = 12', 'FIXED_SHIFT = 11'),
    ('floor phase calls', '(target + delta - 1) // delta', 'target // delta'),
    ('require overshoot at equality', '(target + delta - 1) // delta', '(target + delta) // delta'),
    ('no positive cap', 'delta = min(delta, DELTA_CAP)', 'delta = delta'),
    ('zero-target takes no calls', 'return max(1, (target + delta - 1) // delta)', 'return (target + delta - 1) // delta'),
    ('accept zero length', 'if length < 1 or delta <= 0:', 'if delta <= 0:'),
    ('carry phase overshoot', '[ticks * c for c in defaults]', '[phase_ticks((length - 1) * c + 1) for c in defaults]'),
    ('apply flat timing to every class', 'if kind == FLAT_RIDE else None', 'if True else None'),
    ('PAL frames instead of sim calls', 'TICKS_PER_SECOND = 25', 'TICKS_PER_SECOND = 50'),
]


def main():
    folder = Path(__file__).resolve().parent
    production = folder / 'ride_phases.py'
    original = production.read_text()
    env = dict(os.environ, TPW_PHASE_ARCHIVE=str(Path(sys.argv[1]).resolve()),
               PYTHONDONTWRITEBYTECODE='1')

    def check():
        return subprocess.run([sys.executable, '-B', str(folder / 'test_ride_phases.py')],
                              env=env, capture_output=True, text=True)

    baseline = check()
    if baseline.returncode:
        raise SystemExit('baseline failed:\n' + baseline.stderr)
    survivors = []
    try:
        for name, before, after in MUTATIONS:
            if original.count(before) != 1:
                raise RuntimeError(f'{name}: mutation site is not unique')
            production.write_text(original.replace(before, after))
            try:
                result = check()
                killed = result.returncode != 0 and 'FAILED (' in result.stderr
                print(f'{"KILLED" if killed else "SURVIVED"}: {name}', flush=True)
                if not killed:
                    survivors.append(name)
            finally:
                production.write_text(original)
    finally:
        production.write_text(original)
    restored = check()
    if restored.returncode:
        raise SystemExit('restored baseline failed:\n' + restored.stderr)
    print(f'{len(MUTATIONS) - len(survivors)}/{len(MUTATIONS)} killed; '
          f'{len(survivors)} survivors; source restored; baseline green')
    if survivors:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
