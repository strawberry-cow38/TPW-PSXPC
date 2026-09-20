#!/usr/bin/env python3
"""Read-only binary/record census; outputs only inside this worktree. No size-based record loss.

This is a static search, NOT proof that indirect/computed writes cannot exist.
Run from any directory: python3 tools/audit_influence.py
"""
import collections
import hashlib
import json
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGE = Path('/home/ec2-user/tpw/ext/TPW.BIN')
RIP = Path('/home/ec2-user/tpw/ext/rip')
BASE = 0x80010000
image = IMAGE.read_bytes()
def word(data, offset):
    return struct.unpack_from('<I', data, offset)[0]
def hx(value):
    return f'0x{value:08X}'

# Same code ranges as findings/fable-scripts/callers.py; include J as well as JAL.
code = list(range(BASE, 0x800C0498, 4)) + list(range(0x800E8000, 0x800EF000, 4))
targets = [0x80053554, 0x8005358C, 0x8005CDC8, 0x8005CDE8, 0x800961D8,
           0x8008C3CC, 0x800961E0, 0x8008C3D4]
calls = {hx(t): [] for t in targets}
for pc in code:
    instruction = word(image, pc - BASE)
    if instruction >> 26 not in (2, 3):
        continue
    target = (pc & 0xF0000000) | ((instruction & 0x3FFFFFF) << 2)
    if target in targets:
        calls[hx(target)].append(dict(site=hx(pc), opcode='jal' if instruction >> 26 == 3 else 'j'))
pointers = {hx(t): [hx(BASE + off) for off in range(0, len(image) - 3, 4)
                    if word(image, off) == t] for t in targets}
setters = [hx(BASE + off) for off in range(0, len(image) - 7, 4)
           if word(image, off) == 0x03E00008 and word(image, off + 4) == 0xAC850014]

counts = collections.Counter()
records = []
skipped = []
feature_flags = collections.Counter()
manifest = hashlib.sha256()
for path in sorted(RIP.glob('*.bin')):
    data = path.read_bytes()
    manifest.update(path.name.encode() + b'\0' + hashlib.sha256(data).digest())
    counts['files_inspected'] += 1
    if len(data) < 4:
        skipped.append(dict(file=path.name, reason='missing magic')); continue
    if word(data, 0) != 0x96:
        counts['non_record_files'] += 1; continue
    counts['containers_magic_0x96'] += 1
    if len(data) < 0x18:
        skipped.append(dict(file=path.name, reason='missing header offset')); continue
    offset = word(data, 0x14)
    if offset + 4 > len(data):
        skipped.append(dict(file=path.name, reason='missing record type')); continue
    kind = word(data, offset)
    if not 1 <= kind <= 8:
        counts['other_record_types'] += 1; continue
    counts['attraction_records'] += 1
    counts['four_digit_records' if re.fullmatch(r'\d{4}\.bin', path.name) else 'prefixed_records'] += 1
    counts['type_' + str(kind)] += 1
    old_guard = offset + 0x40 > len(data)
    counts['would_be_skipped_by_old_0x40_guard'] += old_guard
    record = dict(file=path.name, type=kind, record_offset=offset,
                  bytes_after_offset=len(data) - offset, old_guard_would_skip=old_guard)
    if kind == 2:
        # Read only the established byte; do not require any unrelated trailing bytes.
        if offset + 0x2E >= len(data):
            skipped.append(dict(file=path.name, reason='missing feature flag byte'))
        else:
            flag = data[offset + 0x2E]
            feature_flags[flag] += 1
            record['feature_flag_0x2e'] = flag
    records.append(record)
counts['skipped_unreadable'] = len(skipped)
# Positive and negative controls: recover BOTH known writers, and both zero/nonzero feature flags.
controls = dict(
    known_allocator_sites_recovered={x['site'] for x in calls[hx(0x80053554)]}
        == {'0x8008C2C4', '0x800959D4'},
    known_flag_setters_recovered={'0x8008C3CC', '0x800961D8'}.issubset(setters),
    feature_zero_and_nonzero_flags=feature_flags[0] > 0 and any(k for k in feature_flags),
    short_records_actually_read=any(r['old_guard_would_skip'] and 'feature_flag_0x2e' in r for r in records))
result = dict(image=str(IMAGE), image_sha256=hashlib.sha256(image).hexdigest(),
              record_manifest_sha256=manifest.hexdigest(), code_ranges=[[hx(code[0]), '0x800C0498'],
              ['0x800E8000', '0x800EF000']], direct_calls=calls, literal_pointers=pointers,
              identical_flag_setter_leaves=setters, counts=dict(counts), skipped=skipped,
              feature_flag_histogram=dict(sorted(feature_flags.items())), controls=controls,
              records=records,
              limitation='No bit-1 producer established. A negative static search is not a dynamic proof of absence.')
(ROOT / 'findings/influence-audit.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps({k: result[k] for k in ('counts', 'feature_flag_histogram', 'controls')}, indent=2))
if skipped or not all(controls.values()):
    raise SystemExit('Audit incomplete or control failed; inspect influence-audit.json.')
