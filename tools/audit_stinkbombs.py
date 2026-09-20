#!/usr/bin/env python3
"""Bounded, read-only search for a stinkbomb identity in SLES-026.88.

No runtime identity is inferred from a caption, an opcode match, or missing matches.
All outputs stay in this checkout. Every input file/word has accounting, including exclusions.
"""
import collections
import hashlib
import json
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
BASE = 0x80010000
IMAGE = Path('/home/ec2-user/tpw/ext/TPW.BIN')
RIP = Path('/home/ec2-user/tpw/ext/rip')
ARCHIVE = RIP.parent / 'FOLIO.GAZ'
# Same established code ranges as callers.py; also scan the rest of the image separately.
CODE = ((BASE, 0x800C0498), (0x800E8000, 0x800EF000))
TERMS = re.compile(rb'stink|sbomb|hooligan|vandal|troublemak|prank|bomb|naughty|mischief|delinquen|litter|vomit|fart', re.I)


def hx(n):
    return f'0x{n:08X}'


def word(data, off):
    return struct.unpack_from('<I', data, off)[0]


def words(data):
    return ((BASE + off, word(data, off)) for off in range(0, len(data) - 3, 4))


def is_code(pc):
    return any(lo <= pc < hi for lo, hi in CODE)


def direct_calls(data, target):
    return [pc for pc, w in words(data) if w >> 26 in (2, 3)
            and ((pc & 0xF0000000) | ((w & 0x3FFFFFF) << 2)) == target]


def literals(data, target):
    return [pc for pc, w in words(data) if w == target]


def immediate_loads(data, value):
    return [dict(site=hx(pc), register=(w >> 16) & 31, known_code=is_code(pc))
            for pc, w in words(data) if w >> 26 in (9, 13)
            and (w >> 21) & 31 == 0 and w & 0xFFFF == value]


def ascii_hits(data):
    return [dict(offset=m.start(), text=m[0].decode('ascii'))
            for m in re.finditer(rb'[\x20-\x7E]{4,}', data) if TERMS.search(m[0])]


def strings(data):
    count = word(data, 0)
    if 4 + 4 * count > len(data):
        raise ValueError('truncated string offset table')
    result = []
    for i in range(count):
        off = word(data, 4 + 4 * i)
        if off < 4 + 4 * count or off >= len(data):
            raise ValueError('string offset outside payload')
        end = data.index(b'\0', off)
        result.append(dict(id=i, offset=off, text=data[off:end].decode('latin1')))
    return result


def record(data):
    """Read ONLY the magic, offset, type and name id; short records remain eligible."""
    if len(data) < 4:
        return 'truncated', None
    if word(data, 0) != 0x96:
        return 'non_record', None
    if len(data) < 0x18:
        return 'truncated', None
    off = word(data, 0x14)
    if off + 4 > len(data):
        return 'truncated', None
    kind = word(data, off)
    if not 1 <= kind <= 8:
        return 'other_type', dict(type=kind)
    if off + 8 > len(data):
        return 'truncated', None
    return 'attraction', dict(type=kind, offset=off, text_id=word(data, off + 4),
                              old_guard_would_skip=off + 0x40 > len(data))


def rules(archive):
    """READ: FOLIO 1/2, 12-byte schedule, signed-halfword VM at 0x80017024.

    Decode every scheduled program and account for unreferenced code words separately.
    """
    def entry(index):
        off, size = struct.unpack_from('<II', archive, 8 + 8 * index)
        if off + size > len(archive):
            raise ValueError('truncated archive entry')
        return archive[off:off + size]
    schedule, code = entry(1), entry(2)
    if len(schedule) % 12 or len(code) % 2:
        raise ValueError('partial schedule row or code word')
    code = struct.unpack('<' + 'h' * (len(code) // 2), code)
    arity = (0, 2, 2, 2, 2, 2, 2, 1, 1, 1)
    posts, covered = [], set()
    instructions = 0
    for row in range(len(schedule) // 12):
        cursor = struct.unpack_from('<h', schedule, row * 12)[0]
        while True:
            if not 0 <= cursor < len(code):
                raise ValueError('rule cursor outside code')
            op = code[cursor]
            if not 0 <= op < len(arity) or cursor + arity[op] >= len(code):
                raise ValueError('invalid/truncated opcode')
            covered.update(range(cursor, cursor + 1 + arity[op]))
            if op == 0:
                break
            instructions += 1
            if op == 7:
                posts.append(dict(rule=row, word_offset=cursor, message=code[cursor + 1]))
            cursor += 1 + arity[op]
    return dict(programs=len(schedule) // 12, instructions=instructions, posts=posts,
                code_words=len(code), unreferenced_code_words=len(code) - len(covered),
                skipped_programs=0)


def audit():
    data = IMAGE.read_bytes()
    english = strings((RIP / '0407.bin').read_bytes())
    symbols = strings((RIP / '0410.bin').read_bytes())
    counts = collections.Counter()
    names, hits, failures = [], [], []
    manifest = hashlib.sha256()
    for path in sorted(RIP.glob('*.bin')):
        counts['files_enumerated'] += 1
        try:
            payload = path.read_bytes()
        except OSError as exc:
            failures.append(dict(file=path.name, reason=str(exc)))
            continue
        manifest.update(path.name.encode() + b'\0' + hashlib.sha256(payload).digest())
        counts['files_raw_searched'] += 1
        hits.extend(dict(file=path.name, **hit) for hit in ascii_hits(payload))
        classification, rec = record(payload)
        counts[classification] += 1
        if classification == 'truncated':
            failures.append(dict(file=path.name, reason='truncated record fields'))
        elif classification == 'attraction':
            counts['old_guard_would_skip_but_read'] += rec['old_guard_would_skip']
            counts['numeric_names' if re.fullmatch(r'\d{4}\.bin', path.name) else 'prefixed_names'] += 1
            if rec['text_id'] >= len(english):
                failures.append(dict(file=path.name, reason='text id outside English table'))
            else:
                names.append(dict(file=path.name, **rec, name=english[rec['text_id']]['text']))
    counts['skipped_unreadable_or_truncated'] = len(failures)
    targets = (0x800514E0, 0x80051A94, 0x8006694C, 0x80053554,
               0x800926AC, 0x80098494, 0x8008C2B4)
    calls = {hx(t): [dict(site=hx(pc), known_code=is_code(pc))
                     for pc in direct_calls(data, t)] for t in targets}
    script = rules(ARCHIVE.read_bytes())
    messages = []
    # READ advisor.md: 289 rows, stride 20, caption halfword at row+0.
    for i in range(289):
        address = 0x800EE4FC + 20 * i
        tid = struct.unpack_from('<H', data, address - BASE)[0]
        if tid in (68, 484, 864):
            messages.append(dict(message=i, address=hx(address), text_id=tid,
                                 english=english[tid], symbol=symbols[tid]))
    controls = dict(
        litter_three_known_callers=direct_calls(data, 0x800514E0)
            == [0x80053AFC, 0x8008D9B0, 0x80090C2C],
        both_influence_producers=direct_calls(data, 0x80053554) == [0x8008C2C4, 0x800959D4],
        callback_pointer_found=literals(data, 0x8008C2B4) == [0x800F79E0],
        named_caption_found=english[864]['text'].startswith('Some prankster has left a stink bomb'),
        symbol_found=symbols[864]['text'].strip() == 'STR_ADVMES_ADD_PRANK_SBOMB',
        short_bin_read=any(r['file'] == '0019.bin' and r['old_guard_would_skip']
                           and r['name'] == 'Litter Bin' for r in names),
        ordinary_named_records_read=any(r['name'] == 'Security Camera' for r in names),
        live_advisor_rule_found=any(p['message'] == 0 for p in script['posts']),
        prank_rules_absent=not any(p['message'] in (117, 118, 119) for p in script['posts']))
    return dict(
        conclusion='Stinkbomb runtime identity NOT ESTABLISHED; no simulation mapping established.',
        image_sha256=hashlib.sha256(data).hexdigest(),
        record_manifest_sha256=manifest.hexdigest(),
        image_scan=dict(bytes=len(data), aligned_words=len(data) // 4,
                        known_code_words=sum(is_code(pc) for pc, _ in words(data)),
                        other_words_also_scanned=sum(not is_code(pc) for pc, _ in words(data)),
                        trailing_bytes_not_decoded_as_words=len(data) % 4,
                        skipped_aligned_words=0),
        record_counts=dict(counts), failures=failures, controls=controls,
        raw_ascii_hits=dict(executable=ascii_hits(data), extracted_files=hits,
                            files_skipped=counts['files_enumerated'] - counts['files_raw_searched']),
        parsed_text_tables=dict(english_entries=len(english), symbol_entries=len(symbols),
                                skipped_entries=0, tables_selected=2),
        advisor_messages=messages, advisor_table_rows_examined=289, advisor_table_rows_skipped=0,
        direct_jump_or_call_matches=calls,
        literal_pointers={hx(t): [hx(pc) for pc in literals(data, t)] for t in targets},
        immediate_loads={str(i): immediate_loads(data, i) for i in (117, 118, 119)},
        rules=script,
        attraction_names=names,
        attraction_name_keyword_matches=[r for r in names if TERMS.search(r['name'].encode('latin1'))],
        limitations=[
            'ASCII search does not decode compressed data, audio, or graphics; all 690 raw files still searched.',
            'J/JAL, literal and immediate searches do not resolve computed or aliased accesses; data can mimic opcodes.',
            'Only English and symbol tables are parsed; the remaining files receive the raw ASCII search.',
            'No dynamic watchpoints, overlay-code analysis, or executable-to-caption producer link.',
            'Visitor type assignment does not exhaust every flag or prove absence of a prankster class.'])


def main():
    result = audit()
    (ROOT / 'findings/stinkbombs-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: result[k] for k in ('image_scan', 'record_counts', 'controls')}, indent=2))
    print('Rules:', result['rules']['programs'], 'skipped:', result['rules']['skipped_programs'])
    if result['failures'] or not all(result['controls'].values()):
        raise SystemExit('Incomplete audit or failed control; inspect stinkbombs-audit.json.')


if __name__ == '__main__':
    main()
