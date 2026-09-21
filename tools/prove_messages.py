#!/usr/bin/env python3
"""Bounded push-site proof, not a new save-format trace. No game bytes are written.

Check the two branches and every direct call in the guarded block, including the delay slot
that preserves the independent voice gate. Census the already-established 289 table rows.
Run: python3 tools/prove_messages.py [--bin /path/to/TPW.BIN]
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--bin', type=Path, default=Path('/home/ec2-user/tpw/ext/TPW.BIN'))
args = parser.parse_args()
data = args.bin.read_bytes()
BASE = 0x80010000

def word(address):
    return struct.unpack_from('<I', data, address - BASE)[0]

def branch(address, rs, rt, target):
    instruction = word(address)
    assert instruction >> 26 == 4, hex(address)
    assert (instruction >> 21) & 31 == rs and (instruction >> 16) & 31 == rt
    offset = struct.unpack('<h', struct.pack('<H', instruction & 0xFFFF))[0]
    assert address + 4 + offset * 4 == target
    return target

# READ: a2 is the advisor's flag byte, s0 the table's unsigned text id.
assert word(0x80013F1C) == 0x30C20001  # andi v0,a2,1
flag_target = branch(0x80013F20, 2, 0, 0x80013F8C)
assert word(0x80013F44) == 0x94500000  # lhu s0,0(v0)
assert word(0x80013F48) == 0x24030124  # addiu v1,zero,0x124
sentinel_target = branch(0x80013F4C, 16, 3, 0x80013F94)
assert word(0x80013F50) == 0x30C20002  # delay slot: andi v0,a2,2
assert word(0x80013F90) == 0x30C20002  # text-disabled path rejoins voice gate
branch(0x80013F94, 2, 0, 0x8001400C)

calls = []
for address in range(0x80013F54, 0x80013F88, 4):
    instruction = word(address)
    if instruction >> 26 == 3:
        calls.append((address, (address & 0xF0000000) | ((instruction & 0x3FFFFFF) << 2)))
assert calls == [(0x80013F54, 0x8003A7BC), (0x80013F6C, 0x800141BC),
                 (0x80013F74, 0x80050530), (0x80013F80, 0x800385AC)]

cases = []
for flags in range(4):
    for text_id in (0x123, 0x124, 0x125):
        start = flag_target if not flags & 1 else sentinel_target if text_id == 0x124 else 0x80013F54
        reached = [(a, t) for a, t in calls if a >= start]
        expected = 4 if flags & 1 and text_id != 0x124 else 0
        assert len(reached) == expected
        cases.append(dict(flags=flags, text_id=text_id, calls_reached=len(reached),
                          calls_bypassed=4-len(reached), voice_gate_enabled=bool(flags & 2)))
assert sum(c['calls_reached'] == 4 for c in cases) == 4
assert sum(c['calls_reached'] == 0 for c in cases) == 8

established = json.loads((ROOT / 'findings/advisor-messages.json').read_text())
assert established['record_count'] == 289 and len(established['messages']) == 289
sentinels = []
for index, record in enumerate(established['messages']):
    text_id = struct.unpack_from('<H', data, 0x800EE4FC - BASE + 20 * index)[0]
    assert record['id'] == index and record['text_id'] == text_id
    if text_id == 0x124:
        sentinels.append(index)
assert len(sentinels) == 46
audit = dict(binary_sha256=hashlib.sha256(data).hexdigest(),
             scope='0x80013F1C..0x80013F94 and all 289 established text-id fields; no whole-image negative search',
             guarded_words_examined=13, direct_call_words=4, noncall_words=9,
             guarded_calls=[dict(site=hex(a), target=hex(t)) for a, t in calls],
             cases=cases, positive_push_controls=4, bypass_controls=8,
             table_rows_examined=289, table_rows_unexamined=0,
             advisor_pushes_suppressed=46, captioned_positive_controls=243,
             suppressed_message_ids=sentinels,
             result='0x124 bypasses construction, three-field setup, HUD lookup and push; voice gate is independent')
(ROOT / 'findings/messages-proof.json').write_text(json.dumps(audit, indent=2) + '\n')
print('Push-site cases: 12 examined, 0 skipped; 4 push controls, 8 bypass controls.')
print('Table: 289 examined, 0 skipped; 46 suppress the record, 243 captioned controls.')
