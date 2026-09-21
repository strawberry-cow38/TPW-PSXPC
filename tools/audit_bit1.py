#!/usr/bin/env python3
"""Bounded effector-writer audit for SLES-026.88. External inputs are read only.

The pointer-ownership argument is hand traced in findings/influence-bit1.md.
This checks its ingress manifest, every aligned instruction-shaped field store,
and affine aliases in its payload leaves. It is NOT a whole-program alias solver.
"""
import collections
import hashlib
import json
from pathlib import Path
import struct

from audit_prank_reachability import overlay_decode

ROOT = Path(__file__).resolve().parents[1]
IMAGE = Path('/home/ec2-user/tpw/ext/TPW.BIN')
BASE, GP, POOL = 0x80010000, 0x80102654, 0x80103860
STORES = {40: 'sb', 41: 'sh', 42: 'swl', 43: 'sw', 46: 'swr',
          56: 'swc0', 57: 'swc1', 58: 'swc2', 59: 'swc3'}
LOADS = {32, 33, 34, 35, 36, 37, 38, 48, 49, 50, 51}
# READ: include every instruction, not just a guessed stack-prologue entry.
REGIONS = {
    'park_wrappers': (0x80053554, 0x8005366C),
    'pool_operations': (0x8005CD68, 0x8005CF30),
    'pool_construction_links': (0x80060228, 0x80060398),
    'predicate_flags_constructor': (0x800614D8, 0x80061574),
    'entry_link_constructor': (0x80062168, 0x800621A0),
    'particle_payload': (0x8008C3C4, 0x8008C408),
    'entertainer_payload': (0x800961D8, 0x80096214),
}
INGRESS = {
    0x80023CFC: 0x8005358C, 0x800501B8: 0x80060228,
    0x8008C2C4: 0x80053554, 0x8008C314: 0x8008C3D4,
    0x8008C320: 0x8008C3C4, 0x8008C32C: 0x8008C3CC,
    0x8008C378: 0x8005358C, 0x8008FED8: 0x800535B4,
    0x800959D4: 0x80053554, 0x80095A28: 0x800961E0,
    0x80095A34: 0x800961D8, 0x80095A98: 0x8005358C,
    0x80095E5C: 0x8005358C, 0x800960EC: 0x8005358C,
    0x800962A8: 0x8005358C,
}
LEAVES = {
    'particle_flags': (0x8008C3CC, 0x8008C3D4, 0x8008C32C, 0x8008C330),
    'entertainer_flags': (0x800961D8, 0x800961E0, 0x80095A34, 0x80095A38),
}
EXTRA_TARGETS = (0x8008C458, 0x8008C460, 0x8008C2B4, 0x8008C348)
OWNER_WINDOWS = {
    'feature_outer_or_plus8': (0x80022400, 0x80024354, (0x7C, 0x74)),
    'feature_base_plus8': (0x80062500, 0x80063700, (0x74,)),
    'entertainer_outer_or_plus8': (0x80095778, 0x80096320, (0x4C, 0x44)),
    'person_staff_plus8': (0x8009396C, 0x80095778, (0x44,)),
    'emitter_constructor': (0x8008A6D8, 0x8008A774, (0x30,)),
    'emitter_handle_accessors': (0x8008C458, 0x8008C46C, (0x30,)),
}
POOL_REFS = (0x800501C4, 0x80050418, 0x80053554, 0x80053570, 0x80053590, 0x800535C0)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def signed(imm):
    return imm - 65536 if imm & 0x8000 else imm


def word(data, base, pc):
    off = pc - base
    if off < 0 or off % 4 or off + 4 > len(data):
        raise ValueError('outside aligned image')
    return struct.unpack_from('<I', data, off)[0]


def store_bytes(op, offset):
    """Little-endian MIPS byte destinations, assuming the base is word aligned."""
    if op == 40:
        return [offset]
    if op == 41:
        return list(range(offset, offset + 2))
    if op == 42:
        return list(range(offset & ~3, offset + 1))
    if op == 46:
        return list(range(offset, (offset & ~3) + 4))
    if op in (43, 56, 57, 58, 59):
        return list(range(offset, offset + 4))
    raise ValueError('not a store')


def scan(data, base, targets):
    """No code-range, register-base, opcode-width or last-word exclusions."""
    edges, pointers, stores, roots, clones = [], [], [], [], []
    op_counts = collections.Counter()
    for off in range(0, len(data) - 3, 4):
        pc = base + off
        ins = word(data, base, pc)
        op, rs, rt, imm = ins >> 26, (ins >> 21) & 31, (ins >> 16) & 31, signed(ins & 65535)
        if op in (2, 3):
            target = ((pc + 4) & 0xF0000000) | ((ins & 0x3FFFFFF) << 2)
            if target in targets:
                edges.append(dict(site=pc, target=target, kind='JAL' if op == 3 else 'J'))
        if op in (4, 5, 6, 7) or (op == 1 and rt in (0, 1, 16, 17)):
            target = pc + 4 + imm * 4
            if target in targets:
                edges.append(dict(site=pc, target=target, kind='branch'))
        if ins in targets:
            pointers.append(dict(site=pc, target=ins))
        if rs == 28 and GP + imm == POOL and (op in LOADS or op in STORES or op in (8, 9)):
            roots.append(dict(site=pc, opcode=op, register=rt))
        if op in STORES:
            op_counts[STORES[op]] += 1
            touched = store_bytes(op, imm)
            if any(20 <= b < 24 for b in touched):
                stores.append(dict(site=pc, opcode=STORES[op], base_register=rs,
                                   value_register=rt, offset=imm, bytes=touched))
        if ins == 0x03E00008 and off + 8 <= len(data) and word(data, base, pc + 4) == 0xAC850014:
            clones.append(pc)
    return dict(bytes=len(data), aligned_words=len(data)//4, skipped_words=0,
                trailing_bytes=len(data) % 4, edges=edges, pointers=pointers,
                pool_gp_references=roots, store_opcode_counts=dict(op_counts),
                field_store_candidates=stores, identical_setters=clones)


def constant_refs(data, base, wanted):
    """Straight-line constant materialization and memory access, including GP aliases.

    Reset after transfer delay slots: no claim about values merged across CFG
    edges. Unknown operations invalidate results instead of retaining stale LUI.
    """
    regs = [None] * 32
    regs[0], regs[28] = 0, GP
    reset_at, rows = None, []
    for off in range(0, len(data)-3, 4):
        pc, ins = base+off, word(data, base, base+off)
        if reset_at is not None and pc >= reset_at:
            regs = [None] * 32
            regs[0], regs[28], reset_at = 0, GP, None
        op, rs, rt, rd, fn = ins >> 26, (ins >> 21) & 31, (ins >> 16) & 31, (ins >> 11) & 31, ins & 63
        imm, value, dest = signed(ins & 65535), None, None
        if op in LOADS or op in STORES:
            address = add(regs[rs], imm)
            if address in wanted:
                rows.append(dict(site=pc, value=address, kind='memory'))
            if op in LOADS:
                dest = rt
        elif op == 15:
            dest, value = rt, (ins & 65535) << 16
        elif op in (8, 9):
            dest, value = rt, add(regs[rs], imm)
        elif op in (12, 13, 14):
            dest = rt
            if regs[rs] is not None:
                literal = ins & 65535
                value = regs[rs] & literal if op == 12 else regs[rs] | literal if op == 13 else regs[rs] ^ literal
        elif op == 0 and fn in (33, 37):
            dest = rd
            if regs[rs] is not None and regs[rt] is not None:
                value = (regs[rs] + regs[rt]) & 0xFFFFFFFF if fn == 33 else regs[rs] | regs[rt]
        elif op == 0 and fn not in (8, 9, 12, 13, 17, 19, 24, 25, 26, 27):
            dest = rd
        elif op in (10, 11) or (op in (16, 18) and rs in (0, 2)):
            dest = rt
        elif op not in (0, 1, 2, 3, 4, 5, 6, 7, 16, 18):
            regs = [None] * 32  # undecodable word / unsupported instruction
            regs[0], regs[28] = 0, GP
        if dest is not None and dest != 0:
            regs[dest] = value
            if value in wanted:
                rows.append(dict(site=pc, value=value, kind='materialized'))
        if op in (1, 2, 3, 4, 5, 6, 7) or (op == 0 and fn in (8, 9)):
            reset_at = pc + 8
        regs[0] = 0
    return rows


def partition_ingress(rows, regions, expected):
    if not regions or not expected:
        raise ValueError('empty ingress domain')
    inside = lambda pc: any(start <= pc < end for start, end in regions.values())
    external = [r for r in rows if not inside(r['site'])]
    found = {r['site']: r['target'] for r in external}
    return dict(external=external, internal_count=len(rows)-len(external),
                unexpected=[r for r in external if expected.get(r['site']) != r['target']],
                missing=[dict(site=p, target=t) for p, t in expected.items() if found.get(p) != t])


def literal_a1(ins):
    if ins >> 26 in (9, 13) and (ins >> 21) & 31 == 0 and (ins >> 16) & 31 == 5:
        return (signed(ins & 65535) if ins >> 26 == 9 else ins & 65535) & 0xFFFFFFFF
    raise ValueError('flag source is not literal a1')


def add(value, delta):
    if isinstance(value, tuple):
        return (value[0], value[1] + delta)
    return None if value is None else (value + delta) & 0xFFFFFFFF


def leaf_writes(data, base, start, end, flags=None):
    """Affine a0 aliases in small straight-line payload/link leaves, including delay.

    Unsupported operations fail closed. Loads produce unknown scalar values;
    this deliberately cannot prove absence in arbitrary functions or CFGs.
    """
    if start >= end:
        raise ValueError('empty leaf')
    regs = [None] * 32
    regs[0], regs[4], regs[5], regs[6] = 0, ('entry', 0), flags, 1
    writes, returned, lo = [], False, None
    for pc in range(start, end, 4):
        ins = word(data, base, pc)
        op, rs, rt, rd, fn = ins >> 26, (ins >> 21) & 31, (ins >> 16) & 31, (ins >> 11) & 31, ins & 63
        imm = signed(ins & 65535)
        if returned and pc != end - 4:
            raise ValueError('instructions beyond return delay')
        if ins == 0:
            pass
        elif ins == 0x03E00008:
            returned = True
        elif op == 9:
            regs[rt] = add(regs[rs], imm)
        elif op == 13 and isinstance(regs[rs], int):
            regs[rt] = regs[rs] | (ins & 65535)
        elif op == 0 and fn in (33, 37):
            if regs[rt] == 0:
                regs[rd] = regs[rs]
            elif regs[rs] == 0:
                regs[rd] = regs[rt]
            elif fn == 33 and isinstance(regs[rt], int):
                regs[rd] = add(regs[rs], regs[rt])
            else:
                raise ValueError('unsupported alias arithmetic')
        elif op == 0 and fn == 24:
            lo = regs[rs] * regs[rt] & 0xFFFFFFFF if isinstance(regs[rs], int) and isinstance(regs[rt], int) else None
        elif op == 0 and fn == 18:
            regs[rd] = lo
        elif op in LOADS:
            regs[rt] = None
        elif op in STORES:
            address = add(regs[rs], imm)
            if not isinstance(address, tuple) or address[0] != 'entry':
                raise ValueError('store with unbounded destination')
            touched = store_bytes(op, address[1])
            writes.append(dict(site=pc, opcode=STORES[op], bytes=touched, value=regs[rt],
                               flag_overlap=any(20 <= b < 24 for b in touched)))
        else:
            raise ValueError(f'unsupported leaf instruction at {pc:#x}')
        regs[0] = 0
    if not returned or word(data, base, end - 8) != 0x03E00008:
        raise ValueError('missing final return and delay')
    return writes


def flag_domain(data):
    rows = []
    for name, (start, end, caller, source) in LEAVES.items():
        call = word(data, BASE, caller)
        if call >> 26 != 3 or 0x80000000 | ((call & 0x3FFFFFF) << 2) != start:
            raise ValueError('changed flag setter call')
        flags = literal_a1(word(data, BASE, source))
        writes = leaf_writes(data, BASE, start, end, flags)
        relevant = [r for r in writes if r['flag_overlap']]
        if len(relevant) != 1 or relevant[0]['bytes'] != [20, 21, 22, 23]:
            raise ValueError('changed flag write extent')
        rows.append(dict(name=name, caller=caller, source=source, flags=flags,
                         bit1=bool(flags & 1), writes=relevant))
    if len(rows) != 2:
        raise ValueError('empty or incomplete producer domain')
    return rows


def targets():
    return {pc for start, end in REGIONS.values() for pc in range(start, end, 4)}


def patched(data, base, pc, ins):
    word(data, base, pc)  # Bounds before mutation; never negative-index the image.
    result = bytearray(data)
    struct.pack_into('<I', result, pc - base, ins)
    return bytes(result)


def injection_control(data, base):
    """Inject JAL, interior J, literal callback, GP load and an actual flag store."""
    if len(data) < 24:
        raise ValueError('empty control population')
    values = (0x0C000000 | ((0x80053554 >> 2) & 0x3FFFFFF),
              0x08000000 | ((0x800961DC >> 2) & 0x3FFFFFF), 0x800961D8,
              0x8F84120C, 0x24050001, 0xAC850014)
    copy = struct.pack('<6I', *values) + data[24:]
    result = scan(copy, base, targets())
    checks = dict(allocator=any(r['site'] == base and r['target'] == 0x80053554 for r in result['edges']),
                  interior=any(r['site'] == base+4 and r['target'] == 0x800961DC for r in result['edges']),
                  callback=any(r['site'] == base+8 and r['target'] == 0x800961D8 for r in result['pointers']),
                  pool=any(r['site'] == base+12 for r in result['pool_gp_references']),
                  store=any(r['site'] == base+20 for r in result['field_store_candidates']))
    return dict(checks=checks, passed=all(checks.values()), injections=len(values), skipped=0)


def owner_accesses(data, base, start, end, offsets):
    if start >= end or not offsets:
        raise ValueError('empty owner window')
    rows, stack = [], []
    fields = {offset+i for offset in offsets for i in range(4)}
    load_width = {32: 40, 33: 41, 34: 42, 35: 43, 36: 40, 37: 41, 38: 46,
                  48: 56, 49: 57, 50: 58, 51: 59}
    for pc in range(start, end, 4):
        ins = word(data, base, pc)
        op, rs, rt = ins >> 26, (ins >> 21) & 31, (ins >> 16) & 31
        if op in LOADS or op in STORES:
            touched = store_bytes(load_width.get(op, op), signed(ins & 65535))
            if fields.intersection(touched):
                (stack if rs == 29 else rows).append(dict(site=pc, opcode=op, base_register=rs, value_register=rt))
    return dict(start=start, end=end, offsets=offsets, words=(end-start)//4,
                accesses=rows, stack_accesses_excluded=stack, skipped_words=0)


def audit():
    data = IMAGE.read_bytes()
    primary_targets = targets()
    main = scan(data, BASE, primary_targets | {POOL})
    ingress = partition_ingress(main['edges'], REGIONS, INGRESS)
    domain = flag_domain(data)
    constants = constant_refs(data, BASE, primary_targets | {POOL})
    clone_scan = scan(data, BASE, {pc for start in main['identical_setters'] for pc in (start, start+4)})
    clones = [dict(entry=start, store=start+4,
                   edges=[r for r in clone_scan['edges'] if r['target'] in (start, start+4)],
                   pointers=[r for r in clone_scan['pointers'] if r['target'] in (start, start+4)])
              for start in main['identical_setters']]
    payloads = {name: leaf_writes(data, BASE, start, end) for name, (start, end) in {
        'particle_geometry': (0x8008C3D4, 0x8008C408),
        'entertainer_geometry': (0x800961E0, 0x80096214),
        'particle_unknown_word': (0x8008C3C4, 0x8008C3CC),
        'previous_link': (0x80060388, 0x80060390),
        'next_link': (0x80060390, 0x80060398),
    }.items()}
    controls = []
    for name, (_, _, _, source) in LEAVES.items():
        altered = flag_domain(patched(data, BASE, source, 0x24050001))
        controls.append(dict(name=name+' literal -> 1', original_hits=sum(r['bit1'] for r in domain),
                             injected_hits=sum(r['bit1'] for r in altered), passed=sum(r['bit1'] for r in altered) == 1))
    # A new writer, not merely changing the two known inputs. Use an in-memory
    # copy of the geometry leaf; its new alias stores at entry+16+4 == entry+20.
    start, end = 0x800961E0, 0x80096214
    altered = patched(data, BASE, start, 0x24880010)  # addiu t0,a0,16
    altered = patched(altered, BASE, start+4, 0x24050001)
    altered = patched(altered, BASE, start+8, 0xAD050004)  # sw a1,4(t0)
    aliases = [r for r in leaf_writes(altered, BASE, start, end) if r['flag_overlap']]
    controls.append(dict(name='new aliased geometry writer', original_hits=0,
                         injected_writes=aliases, passed=len(aliases) == 1 and aliases[0]['value'] == 1))
    # Explicit high/low construction and a copied GP base are separate controls
    # for a search whose original has no non-GP reference to the pool global.
    probe = struct.pack('<7I', 0x3C088010, 0x25083860, 0x8D040000,
                        0x03804821, 0x8D24120C, 0x3C0A8009, 0x254A61D8)
    found = constant_refs(probe, BASE, {POOL, 0x800961D8})
    controls.append(dict(name='absolute pool, GP alias and computed setter address',
                         injected_references=found, passed=len(found) == 4))
    owners = {name: owner_accesses(data, BASE, *args) for name, args in OWNER_WINDOWS.items()}
    owner_expected = {
        'feature_outer_or_plus8': [0x80023138, 0x80023C7C, 0x80023CEC],
        'feature_base_plus8': [0x80062778, 0x8006345C],
        'entertainer_outer_or_plus8': [0x800959E0, 0x80095A24, 0x80095A30, 0x80095A88, 0x80095AA0,
                                     0x80095C40, 0x80095E4C, 0x80095E64, 0x800960DC, 0x800960F4,
                                     0x80096298, 0x800962B0],
        'person_staff_plus8': [], 'emitter_constructor': [0x8008A764],
        'emitter_handle_accessors': [0x8008C45C, 0x8008C460],
    }
    for name, expected in owner_expected.items():
        if [r['site'] for r in owners[name]['accesses']] != expected:
            raise ValueError('changed owner access manifest: '+name)
    probe = patched(data, BASE, 0x8009396C, 0xAC850044)
    owner_control = owner_accesses(probe, BASE, *OWNER_WINDOWS['person_staff_plus8'])
    controls.append(dict(name='inherited owner offset write', original_hits=0,
                         injected_accesses=owner_control['accesses'], passed=len(owner_control['accesses']) == 1))
    extra_scan = scan(data, BASE, EXTRA_TARGETS)
    extra = {key: extra_scan[key] for key in ('edges', 'pointers', 'aligned_words', 'skipped_words')}
    if ([(r['site'], r['target']) for r in extra['edges']] !=
            [(0x8008C304, 0x8008C458), (0x8008C358, 0x8008C460), (0x8008C370, 0x8008C458)]
            or [(r['site'], r['target']) for r in extra['pointers']] !=
            [(0x800F79E0, 0x8008C2B4), (0x800F79E4, 0x8008C348)]):
        raise ValueError('changed emitter handle/callback graph')
    overlays = []
    packed = (IMAGE.parent / 'TPW.OVL').read_bytes()
    count = struct.unpack_from('<I', packed)[0]
    if count != 12:
        raise ValueError('changed overlay population')
    directory = [struct.unpack_from('<II', packed, 4+8*i) for i in range(count)]
    for i, (directory_word, off) in enumerate(directory):
        end = directory[i+1][1] if i+1 < count else len(packed)
        decoded, consumed = overlay_decode(packed[off:end])
        row = scan(decoded, 0x80114158, primary_targets | set(EXTRA_TARGETS) | {POOL})
        overlays.append(dict(index=i, sha256=digest(decoded), directory_word=directory_word,
                             packed_bytes=end-off, consumed=consumed, unused_packed_bytes=end-off-consumed,
                             **row, constant_references=constant_refs(decoded, 0x80114158, primary_targets | {POOL}),
                             injection=injection_control(decoded, 0x80114158)))
    boot_data = (IMAGE.parent / 'SLES_026.88').read_bytes()
    if boot_data[:8] != b'PS-X EXE':
        raise ValueError('unknown boot header')
    boot_base, boot_size = struct.unpack_from('<II', boot_data, 24)
    if len(boot_data) != 2048 + boot_size:
        raise ValueError('unaccounted boot bytes')
    boot = dict(sha256=digest(boot_data), base=boot_base, header_bytes_excluded=2048,
                **scan(boot_data[2048:], boot_base, primary_targets | set(EXTRA_TARGETS) | {POOL}),
                constant_references=constant_refs(boot_data[2048:], boot_base, primary_targets | {POOL}),
                injection=injection_control(boot_data[2048:], boot_base))
    regions = {name: dict(start=a, end=b, words=(b-a)//4, sha256=digest(data[a-BASE:b-BASE]))
               for name, (a, b) in REGIONS.items()}
    if (ingress['unexpected'] or ingress['missing'] or main['pointers'] or any(r['bit1'] for r in domain)
            or [r['site'] for r in main['pool_gp_references']] != list(POOL_REFS)
            or [r['site'] for r in constants] != list(POOL_REFS)
            or any(w['flag_overlap'] for rows in payloads.values() for w in rows)
            or not all(c['passed'] for c in controls) or not injection_control(data, BASE)['passed']
            or any(o['edges'] or o['pointers'] or o['pool_gp_references'] or o['constant_references'] or o['unused_packed_bytes']
                   or o['trailing_bytes'] or not o['injection']['passed'] for o in overlays)
            or boot['edges'] or boot['pointers'] or boot['pool_gp_references'] or boot['constant_references'] or not boot['injection']['passed']):
        raise ValueError('changed evidence or failed positive control; reopen the trace')
    return dict(conclusion='Bounded negative: the enumerated intact effector ownership routes write flags 2 or 4, never bit 1.',
                image_sha256=digest(data), main=main, constant_references=constants, ingress=ingress, flag_domain=domain,
                identical_setter_census=clones,
                payload_leaf_writes=payloads, owner_windows=owners, extra_target_census=extra,
                regions=regions, controls=controls, main_injection=injection_control(data, BASE),
                overlays=overlays, overlays_skipped=0, overlay_archive_sha256=digest(packed),
                overlay_directory_bytes_excluded=directory[0][1], boot=boot,
                limitations=['Pointer ownership/control flow is hand traced; this is not a general alias or computed-target proof.',
                             'Syntactic field stores are candidates, not 1:1 effector writes. Their counts include data and other objects.',
                             'No live emulator run. Corruption, modified code/assets/saves and manufactured pointer routes are outside the bound.',
                             'Only TPW.BIN, all 12 TPW.OVL payloads and the boot payload are executable inputs to this census.'])


def main():
    result = audit()
    (ROOT / 'findings/influence-bit1-audit.json').write_text(json.dumps(result, indent=2)+'\n')
    for name, row in [('main', result['main']), ('boot', result['boot'])]:
        print(name, 'words:', row['aligned_words'], 'skipped:', row['skipped_words'],
              'trailing:', row['trailing_bytes'], 'field-store candidates:', len(row['field_store_candidates']))
    print('Overlays:', len(result['overlays']), 'words:', sum(r['aligned_words'] for r in result['overlays']),
          'field-store candidates:', sum(len(r['field_store_candidates']) for r in result['overlays']), 'skipped: 0')
    print('Ingress:', len(result['ingress']['external']), 'unexpected:', len(result['ingress']['unexpected']),
          'missing:', len(result['ingress']['missing']))
    print('Flag writers:', len(result['flag_domain']), 'values:', [r['flags'] for r in result['flag_domain']],
          'bit-1 writers:', sum(r['bit1'] for r in result['flag_domain']))
    print('Domain/alias controls:', len(result['controls']), 'per-image controls:', 2+len(result['overlays']), 'skipped: 0')
    print(result['conclusion'])


if __name__ == '__main__':
    main()
