#!/usr/bin/env python3
"""Bounded queue-caller audit, NOT a prank simulation or a proof of unreachability.

The source-address manifest records hand-read control flow. This tool checks its
coverage and reads its literals; it does not pretend to solve arbitrary dataflow.
Inputs outside this worktree are read only. Run with PYTHONDONTWRITEBYTECODE=1.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

import audit_stinkbombs as prior

ROOT = Path(__file__).resolve().parents[1]
BASE = prior.BASE
SETTER = 0x8001412C  # READ: sh a1,0(a0), at 0x80014130.
SUBMIT = 0x80014144  # READ: ann.py 80014144 8001416C; before the prologue!
TARGETS = (0x80013D30, 0x80013C1C, SUBMIT, 0x8001414C, SETTER,
           0x80014118, 0x8001408C, 0x800677B8, 0x80014288)
PRANK_IDS = (117, 118, 119)  # READ: advisor rows 0x800EEE20/34/48.

# READ: setter call -> EVERY literal a1 assignment reaching it in the hand-read
# functions. Addresses, not guessed ids: the ids are read from TPW.BIN below.
# Branch alternatives and MIPS delay slots are included explicitly.
FIXED_SOURCES = {
    0x80013264: (0x8001325C, 0x80013260),
    0x8001786C: (0x80017870,),
    0x80017938: (0x800178E4, 0x800178F0, 0x800178FC, 0x80017908,
                 0x80017914, 0x80017920, 0x8001792C, 0x80017934),
    0x800179AC: (0x800179B0,),
    0x80017A0C: (0x80017A10,),
    0x80017A6C: (0x80017A70,),
    0x80017ACC: (0x80017AD0,),
    0x80017B2C: (0x80017B30,),
    0x80017B8C: (0x80017B90,),
    0x80017BEC: (0x80017BF0,),
    0x80017C4C: (0x80017C50,),
    0x80017CAC: (0x80017CB0,),
    0x80017D0C: (0x80017D10,),
    0x80017D6C: (0x80017D70,),
    0x80017DCC: (0x80017DD0,),
    0x80017E2C: (0x80017E30,),
    0x80017E8C: (0x80017E90,),
    0x80017EEC: (0x80017EF0,),
    0x80017F4C: (0x80017F50,),
    0x80017FAC: (0x80017FB0,),
    0x8001800C: (0x80018010,),
    0x8001806C: (0x80018070,),
    0x800180CC: (0x800180D0,),
    0x8001812C: (0x80018130,),
    0x8001818C: (0x80018190,),
    0x800181EC: (0x800181F0,),
    0x8001824C: (0x80018250,),
    0x8001C9D8: (0x8001C9BC, 0x8001C9D4),
    0x8001CB84: (0x8001CB88,),
    0x8001CD0C: (0x8001CCF0, 0x8001CD08),
    0x8001CE7C: (0x8001CE60, 0x8001CE78),
    0x8001D00C: (0x8001D010,),
    0x8001D038: (0x8001D03C,),
    0x8001E824: (0x8001E7B4, 0x8001E7D0, 0x8001E7EC, 0x8001E808, 0x8001E820),
    0x8001F088: (0x8001F08C,),
    0x80021E88: (0x80021E8C,),
    0x800622B4: (0x800622B8,),
    0x800631BC: (0x800631C0,),
    0x80066D94: (0x80066D68, 0x80066D74, 0x80066D80, 0x80066D8C, 0x80066D90),
    0x80075B08: (0x80075B0C,),
    0x80079B54: (0x80079AFC, 0x80079B50),
    0x8009C078: (0x8009C040, 0x8009C048, 0x8009C054, 0x8009C060, 0x8009C06C, 0x8009C074),
    0x8009C750: (0x8009C754,),
    0x800B9AFC: (0x800B9ADC, 0x800B9AF8),
    0x800B9B88: (0x800B9B8C,),
}

# READ interpretations with explicitly bounded domains; see findings/pranks.md.
DYNAMIC = {
    0x800140EC: '0x8001408C searches captions starting at id 221; NO upper bound. Normal tutorial rows 221..288, arbitrary input unresolved.',
    0x80017198: '0x80017184 loads the VM operand; 125 scheduled programs audited separately, unscheduled inputs unresolved.',
    0x800677DC: 'a1 forwarded from nine callers of 0x800677B8 in 0x80067928: ids 175..182 and 188.',
    0x80067F28: '0x80102E68[staffKind-1] + 50; loop 0x80067E28..0x80067F5C bounds staffKind to 1..5.',
    0x8006813C: 'Same staff permutation + 30/35/40/50; branches 0x80067FE8..0x80068140.',
    0x8006819C: 'Same staff permutation + 45 at 0x80068180..0x800681A0.',
    0x8009C7D0: 'Return of 0x80014288: 63/64/65 loaded at 0x8001429C/B0/C0/D4.',
}


def hx(value):
    return f'0x{value:08X}'


def scan(data, base, targets):
    """Decode every aligned word; opcodes found in data are still just matches."""
    calls, pointers = [], []
    for off in range(0, len(data) - 3, 4):
        pc = base + off
        word = struct.unpack_from('<I', data, off)[0]
        if word >> 26 in (2, 3):
            target = ((pc + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
            if target in targets:
                calls.append(dict(site=pc, target=target, kind='JAL' if word >> 26 == 3 else 'J'))
        if word in targets:
            pointers.append(dict(site=pc, target=word))
    return dict(bytes=len(data), aligned_words=len(data) // 4, skipped_aligned_words=0,
                trailing_bytes_not_decoded=len(data) % 4, calls=calls, pointers=pointers)


def literal_a1(word):
    """Low halfword stored by 0x80014130; None means NOT a literal a1 assignment."""
    if word >> 26 in (9, 13) and (word >> 21) & 31 == 0 and (word >> 16) & 31 == 5:
        return word & 0xFFFF
    return None


def classify(data, base, sites, fixed, dynamic):
    """Check a hand-read manifest without silently discarding unknown/stale sites."""
    if set(fixed) & set(dynamic):
        raise ValueError('overlapping classifications')
    rows = []
    for site in sites:
        if site in fixed:
            sources = []
            for address in fixed[site]:
                off = address - base
                if off < 0 or off + 4 > len(data) or off % 4:
                    raise ValueError('source outside aligned image')
                value = literal_a1(struct.unpack_from('<I', data, off)[0])
                if value is None:
                    raise ValueError('manifest source is no longer a literal a1 assignment')
                sources.append(dict(address=address, id=value))
            if not sources:
                raise ValueError('empty fixed-source manifest')
            rows.append(dict(site=site, kind='fixed', sources=sources))
        elif site in dynamic:
            rows.append(dict(site=site, kind='computed', reading=dynamic[site]))
        else:
            rows.append(dict(site=site, kind='UNREVIEWED'))
    return dict(rows=rows, unreviewed=sum(r['kind'] == 'UNREVIEWED' for r in rows),
                stale_manifest_sites=sorted((set(fixed) | set(dynamic)) - set(sites)))


def fixed_prank_hits(classification):
    return [dict(site=r['site'], **s) for r in classification['rows']
            for s in r.get('sources', []) if s['id'] in PRANK_IDS]


def missing_jump_sources(data, base, fixed):
    """Independent guard for literal delay-slot alternatives jumping to a setter.

    This covers direct J entries, not general conditional/indirect control flow.
    A source manifest that forgot such a branch must not report completeness.
    """
    missing = []
    for edge in scan(data, base, fixed)['calls']:
        if edge['kind'] != 'J':
            continue
        source = edge['site'] + 4
        off = source - base
        value = literal_a1(struct.unpack_from('<I', data, off)[0]) if off + 4 <= len(data) else None
        if value is not None and source not in fixed[edge['target']]:
            missing.append(dict(jump=edge['site'], setter=edge['target'], source=source, id=value))
    return missing


def audit():
    data = prior.IMAGE.read_bytes()
    main = scan(data, BASE, TARGETS)
    sites = [c['site'] for c in main['calls'] if c['target'] == SETTER]
    classified = classify(data, BASE, sites, FIXED_SOURCES, DYNAMIC)
    # Paired measurement: original posts 120 here; in-memory control changes only
    # its literal to 117. The disc and production code are never patched.
    injected = bytearray(data)
    struct.pack_into('<I', injected, 0x800622B8 - BASE, 0x24050075)
    injected_hits = fixed_prank_hits(classify(injected, BASE, sites, FIXED_SOURCES, DYNAMIC))

    # Existing READ decoder of 0x800BFD9C. Import its functions, never its CLI
    # (the CLI writes outside this checkout). Record the dependency's hash.
    decoder = Path('/home/ec2-user/tpw/fable/ovl_lz.py')
    sys.path.insert(0, str(decoder.parent))
    from ovl_lz import entries, lz
    packed, directory = entries()
    overlays = []
    for index, (offset, directory_word) in enumerate(directory):
        decoded = lz(packed[offset:])
        row = scan(decoded, 0x80114158, TARGETS)
        # Positive control: real string-lookup calls in five decoded overlays.
        control = scan(decoded, 0x80114158, (0x8006F00C,))
        overlays.append(dict(index=index, packed_offset=offset, directory_word=directory_word,
                             sha256=hashlib.sha256(decoded).hexdigest(),
                             string_lookup_control=control['calls'], **row))
    rules = prior.rules(prior.ARCHIVE.read_bytes())
    english = prior.strings((prior.RIP / '0407.bin').read_bytes())
    symbols = prior.strings((prior.RIP / '0410.bin').read_bytes())
    messages = []
    for ident in PRANK_IDS:
        address = 0x800EE4FC + ident * 20
        tid = struct.unpack_from('<H', data, address - BASE)[0]
        messages.append(dict(id=ident, address=address, text_id=tid,
                             english=english[tid], symbol=symbols[tid]))
    wrapper = [c['site'] for c in main['calls'] if c['target'] == 0x800677B8]
    wrapper_sources = {site: (site + 4,) for site in wrapper}
    wrapper_rows = classify(data, BASE, wrapper, wrapper_sources, {})
    controls = dict(
        known_queue_edge=any(c['site'] == 0x80013C54 and c['target'] == 0x80013D30 for c in main['calls']),
        original_fixed_prank_hits=fixed_prank_hits(classified),
        injected_117_hits=injected_hits,
        real_overlay_string_calls=sum(len(o['string_lookup_control']) for o in overlays),
        callback_pointer=scan(data, BASE, (0x8008C2B4,))['pointers'],
        rule_zero_posts=[p for p in rules['posts'] if p['message'] == 0])
    missing_branches = missing_jump_sources(data, BASE, FIXED_SOURCES)
    complete = not classified['unreviewed'] and not classified['stale_manifest_sites'] and not missing_branches
    if not (complete and controls['known_queue_edge'] and not controls['original_fixed_prank_hits']
            and injected_hits == [dict(site=0x800622B4, address=0x800622B8, id=117)]
            and controls['real_overlay_string_calls'] > 0 and controls['rule_zero_posts']
            and controls['callback_pointer'] == [dict(site=0x800F79E0, target=0x8008C2B4)]):
        raise ValueError('incomplete manifest or failed paired control')
    return dict(conclusion='Producer NOT ESTABLISHED. Stop condition: no simulation component added.',
                image_sha256=hashlib.sha256(data).hexdigest(), main=main,
                setter_classification=classified, missing_direct_jump_sources=missing_branches,
                award_wrapper=wrapper_rows,
                staff_permutation_address=0x80102E68,
                staff_permutation=list(data[0x80102E68-BASE:0x80102E6D-BASE]),
                overlays=overlays, overlays_skipped=0,
                overlay_archive_sha256=hashlib.sha256(packed).hexdigest(),
                overlay_decoder_sha256=hashlib.sha256(decoder.read_bytes()).hexdigest(),
                scheduled_rules=rules, messages=messages, controls=controls,
                text_tables_parsed=2, parsed_entries_skipped=0,
                advisor_rows_selected=3, other_advisor_rows_not_reanalysed=286,
                excluded_files=[dict(file='SLES_026.88', bytes=49152, reason='boot executable not analysed')],
                limitations=[
                    'No proof against computed/aliased queue entry or a direct write to queue memory.',
                    'Manifest coverage is checked; its control-flow interpretation was read by hand, not formally proved.',
                    'Unbounded caption lookup and unscheduled VM inputs are not proved safe or unreachable.',
                    'Overlay decoder is an existing dependency, not independently validated in this run.',
                    'No retail/emulator run, actor identity, allocation, cleaning route, or balloon mechanic established.'])


def main():
    result = audit()
    (ROOT / 'findings/pranks-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    print('Main words:', result['main']['aligned_words'], 'skipped:', result['main']['skipped_aligned_words'])
    rows = result['setter_classification']['rows']
    print('Setter sites:', len(rows), 'fixed:', sum(r['kind'] == 'fixed' for r in rows),
          'computed:', sum(r['kind'] == 'computed' for r in rows),
          'unreviewed:', result['setter_classification']['unreviewed'])
    print('Overlays:', len(result['overlays']), 'skipped:', result['overlays_skipped'],
          'words:', sum(o['aligned_words'] for o in result['overlays']))
    print('Fixed prank hits: original', len(result['controls']['original_fixed_prank_hits']),
          'injected control', len(result['controls']['injected_117_hits']), 'skipped: 0')
    print(result['conclusion'])


if __name__ == '__main__':
    main()
