#!/usr/bin/env python3
"""Close the seven computed advisor-ID paths for the original PAL build.

READ addresses below are TPW.BIN addresses, not guessed simulation constants.
The data readers check the hand-traced domain argument in the companion finding;
this is not a general MIPS verifier or a proof about corrupt saves/memory.
External inputs are read only. Outputs stay in this checkout.
"""
import hashlib
import json
from pathlib import Path
import struct

import audit_prank_callers as callers

ROOT = Path(__file__).resolve().parents[1]
BASE = callers.BASE
GP = 0x80102654
PRANKS = {117, 118, 119}


def digest(data):
    return hashlib.sha256(data).hexdigest()


class Image:
    def __init__(self, data, base=BASE):
        self.data, self.base = data, base

    def read(self, address, size):
        off = address - self.base
        if off < 0 or off + size > len(self.data):
            raise ValueError('read outside image')
        return self.data[off:off + size]

    def word(self, address):
        if address % 4:
            raise ValueError('unaligned instruction')
        return struct.unpack('<I', self.read(address, 4))[0]

    def half(self, address):
        return struct.unpack('<H', self.read(address, 2))[0]

    def imm(self, address, op, rs, rt, signed=True):
        word = self.word(address)
        if (word >> 26, (word >> 21) & 31, (word >> 16) & 31) != (op, rs, rt):
            raise ValueError(f'changed instruction at {address:#x}')
        value = word & 0xffff
        return value - 65536 if signed and value & 0x8000 else value

    def pair(self, high, low, register):
        return ((self.imm(high, 15, 0, register, False) << 16)
                + self.imm(low, 9, register, register)) & 0xffffffff


def hits(ids):
    return sorted(set(ids) & PRANKS)


def literal_arm(image, start, setter, submit, default):
    """READ: tutorial switch arms contain a0 setup, a1 literal, J + delay.

    Reject every unfamiliar instruction/loop instead of inventing a default.
    """
    pc, value, pending = start, default, None
    visited = []
    while pc not in (setter, submit):
        if pc in visited:
            raise ValueError('switch arm loop')
        visited.append(pc)
        word = image.word(pc)
        next_pc = pc + 4
        literal = callers.literal_a1(word)
        if literal is not None:
            value = literal
        elif word == 0 or word == 0x27A40010:  # nop / addiu a0,sp,0x10
            pass
        elif word >> 26 == 2 and pending is None:
            pending = ((pc + 4) & 0xf0000000) | ((word & 0x3ffffff) << 2)
            pc += 4
            # Execute the delay instruction before taking the jump.
            delay = image.word(pc)
            visited.append(pc)
            literal = callers.literal_a1(delay)
            if literal is not None:
                value = literal
            elif delay != 0:
                raise ValueError('unknown switch delay instruction')
            next_pc, pending = pending, None
        else:
            raise ValueError('unknown switch arm instruction')
        pc = next_pc
    return dict(id=value, exit=pc, instructions=visited)


def tutorial_switch(image):
    count = image.imm(0x800178B4, 11, 16, 3, False)
    address = image.pair(0x800178BC, 0x800178C0, 2)
    default = image.imm(0x8001411C, 9, 0, 3, False)
    rows = []
    for index in range(count):
        target = image.word(address + index * 4)
        row = literal_arm(image, target, 0x80017938, 0x80017940, default)
        rows.append(dict(index=index, target=target, **row))
    return dict(address=address, rows=rows, outside_unsigned_domain_id=default,
                ids=sorted({default} | {r['id'] for r in rows}), skipped_entries=0)


def first_caption(captions, caption, start):
    """Finite proof witness: first match inside supplied rows, or fail closed.

    The retail loop has no bound. A missing witness must never become 'no prank'.
    """
    for index, text_id in enumerate(captions):
        if text_id == caption:
            return start + index
    raise ValueError('caption has no in-table termination witness')


def replay_domain(image):
    start = image.imm(0x800140AC, 9, 0, 16, False)
    table = image.pair(0x80014090, 0x80014094, 2)
    stride = image.imm(0x800140B4, 9, 3, 3)
    first_offset = image.imm(0x800140A0, 37, 2, 3)
    loop_offset = image.imm(0x800140B0, 9, 2, 3)
    low = -image.imm(0x80013CEC, 9, 3, 2)
    count = image.imm(0x80013CF4, 11, 2, 2, False)
    queue_low = -image.imm(0x80013DC8, 9, 8, 2)
    queue_count = image.imm(0x80013DD0, 11, 2, 2, False)
    if (low, count) != (queue_low, queue_count):
        raise ValueError('queue and interrupt kind domains differ')
    if first_offset != start * stride or loop_offset != first_offset or start > low:
        raise ValueError('replay row/index mismatch')
    # Include captionless rows as a conservative superset, then mark the actual
    # generated cards separately (show skips text 292 at 0x80013F48..4C).
    captions = [image.half(table + stride * i) for i in range(start, low + count)]
    sentinel = image.imm(0x80013F48, 9, 0, 3, False)
    rows = []
    for ident in range(low, low + count):
        caption = image.half(table + ident * stride)
        rows.append(dict(source_id=ident, text_id=caption,
                         creates_card=caption != sentinel,
                         replay_id=first_caption(captions, caption, start)))
    return dict(table=table, stride=stride, start=start, kind3_low=low, kind3_count=count,
                rows=rows, ids=sorted({r['replay_id'] for r in rows if r['creates_card']}),
                conservative_ids=sorted({r['replay_id'] for r in rows}),
                captionless_rows=sum(not r['creates_card'] for r in rows),
                skipped_rows=0)


def archive_entry(archive, index):
    if len(archive) < 16 + 8 * index:
        raise ValueError('truncated archive directory')
    off, size = struct.unpack_from('<II', archive, 8 + 8 * index)
    if off + size > len(archive):
        raise ValueError('truncated archive entry')
    return off, archive[off:off + size]


def decode_programs(schedule, code):
    """READ 0x80017024..224: sequential VM; conditionals return, never branch.

    All predicates are allowed to succeed, so IDs are an overapproximation of
    possible posts. Validate stores too: scripts must not rewrite the scheduler.
    """
    if len(schedule) % 12 or len(code) % 2:
        raise ValueError('partial schedule or code')
    words = struct.unpack('<' + 'h' * (len(code) // 2), code)
    arity = (0, 2, 2, 2, 2, 2, 2, 1, 1, 1)
    covered, rows, posts, writes = set(), [], [], []
    for index in range(len(schedule) // 12):
        cursor = struct.unpack_from('<h', schedule, index * 12)[0]
        instructions = []
        while True:
            if not 0 <= cursor < len(words):
                raise ValueError('program outside code')
            op = words[cursor]
            if not 0 <= op < len(arity) or cursor + arity[op] >= len(words):
                raise ValueError('unknown or truncated opcode')
            args = list(words[cursor + 1:cursor + 1 + arity[op]])
            covered.update(range(cursor, cursor + 1 + arity[op]))
            instructions.append(dict(word=cursor, op=op, args=args))
            if op == 0:
                break
            if op in (1, 2, 3, 4, 5, 6) and not 0 <= args[0] < 72:
                raise ValueError('VM access outside cache')
            if op in (5, 6):
                offsets = [2 * args[0]]
                if op == 6 and args[0] >= 51:
                    offsets.append(144 + 2 * (args[0] - 51))
                if any(off >= 184 for off in offsets):
                    raise ValueError('VM write reaches scheduler state')
                writes.append(dict(rule=index, word=cursor, op=op, slot=args[0],
                                   value=args[1], object_offsets=offsets))
            if op == 7:
                posts.append(dict(rule=index, word=cursor, id=args[0] & 0xffff))
            cursor += 1 + arity[op]
        rows.append(dict(index=index, instructions=instructions))
    return dict(programs=rows, posts=posts, writes=writes,
                ids=sorted({p['id'] for p in posts}), code_words=len(words),
                unreferenced_code_words=len(words) - len(covered), skipped_programs=0)


def staff_ids(permutation, offsets):
    # READ: lbu then sll/sra 24 sign-extends the byte before the addition.
    return sorted({((b - 256 if b & 128 else b) + off) & 0xffff
                   for b in permutation for off in offsets})


def staff_domain(image):
    address = GP + image.imm(0x80067F0C, 9, 28, 3)
    first = image.imm(0x80067E28, 9, 0, 17)
    end = image.imm(0x80067F58, 10, 17, 2)
    guard = image.imm(0x80067E34, 11, 18, 2, False)
    if first != 1 or end - first != guard:
        raise ValueError('staff loop and index guard disagree')
    permutation = list(image.read(address, end - first))
    switch_address = image.pair(0x80067FC8, 0x80067FCC, 2)
    arm_count = image.imm(0x80067FC0, 11, 4, 3, False)
    arms = [image.word(switch_address + 4 * i) for i in range(arm_count)]
    sources = (0x80068018, 0x80068054, 0x80068090, 0x800680CC, 0x80068108, 0x80068138)
    expected_arms = (0x80067FE8, 0x8006801C, 0x80068058, 0x80068094, 0x800680D0, 0x8006810C)
    if tuple(arms) != expected_arms:
        raise ValueError('changed staff switch destinations')
    offsets = [image.imm(pc, 9, 5, 5) for pc in sources]
    a = image.imm(0x80067F24, 9, 5, 5)
    c = image.imm(0x80068198, 9, 5, 5)
    return dict(address=address, permutation=permutation, staff_kinds=list(range(first, end)),
                switch_address=switch_address, switch_targets=arms, offsets=offsets,
                paths={str(0x80067F28): staff_ids(permutation, [a]),
                       str(0x8006813C): staff_ids(permutation, offsets),
                       str(0x8006819C): staff_ids(permutation, [c])},
                skipped_bytes=0, skipped_arms=0)


def overlay_decode(source):
    """Independent bounded transcription of READ 0x800BFD9C..E68.

    Checks packed extents and backward-copy validity; reports consumed bytes.
    """
    output, cursor, flags = bytearray(), 0, 0

    def byte():
        nonlocal cursor
        if cursor >= len(source):
            raise ValueError('truncated overlay')
        value = source[cursor]
        cursor += 1
        return value

    while True:
        flags >>= 1
        if flags & 0xff00 == 0:
            flags = byte() | 0xff00
        if flags & 1 == 0:
            output.append(byte())
            continue
        token = byte()
        if token >= 0x60:
            distance, length = 256 - token, 2
        else:
            distance = ((token & 15) << 8) | byte()
            if distance == 0:
                return bytes(output), cursor
            nibble = token >> 4
            length = byte() + 8 if nibble == 5 else nibble + 3
        if distance > len(output):
            raise ValueError('invalid overlay back reference')
        for _ in range(length):
            output.append(output[-distance])


# These are whole manually read regions. Hashes bind the prose's input-domain
# argument to this image; they do not claim automatic analysis of all stores.
REGIONS = {
    'queue_kind_and_copy': (0x80013C1C, 0x80013EFC),
    'show_replay_builders': (0x80013EFC, 0x80014288),
    'breakdown_selector': (0x80014288, 0x800142E8),
    'vm_initialization_scheduler': (0x80016718, 0x80016A50),
    'vm_interpreter': (0x80017024, 0x80017228),
    'tutorial_switch': (0x8001781C, 0x80017980),
    'card_append_copy': (0x8003A484, 0x8003A52C),
    'card_replay_and_move': (0x8003B878, 0x8003B9E4),
    'award_forwarder': (0x800677B8, 0x80067808),
    'staff_domain': (0x80067DF0, 0x800681E4),
    'save_cards': (0x800719CC, 0x80071BD0),
    'load_cards': (0x800729A0, 0x80072B54),
    'load_card_setters': (0x80072E90, 0x80072F90),
    'overlay_decompressor': (0x800BFD9C, 0x800BFE6C),
}

EXTRA_TARGETS = (0x80017024, 0x80016718, 0x80067F84, 0x800141BC,
                 0x80014278, 0x80014280, 0x800385AC, 0x8003A484,
                 0x80072E90, 0x80072EEC, 0x80072F60, 0x80072F68,
                 0x800693F0, 0x800694F8, 0x800729A0, 0x80013C80)


def patch_word(data, address, value):
    result = bytearray(data)
    struct.pack_into('<I', result, address - BASE, value)
    return result


def audit():
    data = callers.prior.IMAGE.read_bytes()
    image = Image(data)
    archive = callers.prior.ARCHIVE.read_bytes()
    schedule_entry = image.imm(0x8001675C, 9, 0, 5, False)
    code_entry = image.imm(0x80016778, 9, 0, 5, False)
    schedule_off, schedule = archive_entry(archive, schedule_entry)
    code_off, code = archive_entry(archive, code_entry)
    vm = decode_programs(schedule, code)
    tutorial = tutorial_switch(image)
    replay = replay_domain(image)
    staff = staff_domain(image)
    targets = tuple(dict.fromkeys(callers.TARGETS + EXTRA_TARGETS))
    main_scan = callers.scan(data, BASE, targets)
    award_calls = [r['site'] for r in main_scan['calls'] if r['target'] == 0x800677B8]
    award_ids = [image.imm(pc + 4, 9, 0, 5, False) for pc in award_calls]
    selector_sources = (0x8001429C, 0x800142B0, 0x800142C0, 0x800142D4)
    selector_ids = [image.imm(pc, 9, 0, 2, False) for pc in selector_sources]
    paths = [dict(setter=0x800140EC, ids=replay['ids'], examined_rows=len(replay['rows']), skipped=0),
             dict(setter=0x80017198, ids=vm['ids'], examined_posts=len(vm['posts']), skipped=0),
             dict(setter=0x800677DC, ids=sorted(set(award_ids)), examined_callers=len(award_calls), skipped=0)]
    paths += [dict(setter=int(site), ids=ids, examined_bytes=len(staff['permutation']), skipped=0)
              for site, ids in staff['paths'].items()]
    paths.append(dict(setter=0x8009C7D0, ids=sorted(set(selector_ids)),
                      examined_return_sources=len(selector_sources), skipped=0))
    for path in paths:
        path['prank_ids'] = hits(path['ids'])

    # Actual-byte positive controls. None modifies disc, executable or core code.
    controls = []
    injected = bytearray(archive)
    first_post = vm['posts'][0]
    for prank in sorted(PRANKS):
        struct.pack_into('<h', injected, code_off + 2 * (first_post['word'] + 1), prank)
        _, injected_code = archive_entry(injected, code_entry)
        result = decode_programs(schedule, injected_code)
        controls.append(dict(name=f'VM operand -> {prank}', original_hits=hits(vm['ids']),
                             injected_hits=hits(result['ids']),
                             matching_posts=sum(p['id'] == prank for p in result['posts']), skipped=0))
    for site, offset in ((0x80067F28, 50), (0x8006813C, 30), (0x8006819C, 45)):
        injected = bytearray(data)
        injected[staff['address'] - BASE] = 117 - offset
        result = staff_domain(Image(injected))
        controls.append(dict(name=f'staff table, setter {site:#x}', original_hits=hits(staff['paths'][str(site)]),
                             injected_hits=hits(result['paths'][str(site)]), skipped=0))
    for name, source, ids in (('award delay slot', award_calls[0] + 4, award_ids),
                              ('breakdown return', selector_sources[0], selector_ids)):
        word = image.word(source)
        injected = Image(patch_word(data, source, (word & 0xffff0000) | 117))
        controls.append(dict(name=name, original_hits=hits(ids),
                             injected_hits=hits([injected.imm(source, 9, 0, (word >> 16) & 31, False)]), skipped=0))
    injected = patch_word(data, 0x800178E4, 0x24050075)
    controls.append(dict(name='tutorial table target delay slot', original_hits=hits(tutorial['ids']),
                         injected_hits=hits(tutorial_switch(Image(injected))['ids']), skipped=0))
    # Replay sensitivity requires moving its starting row as well as making that
    # row match an eligible tutorial caption. A data-only caption change cannot
    # make a search starting at 221 return an earlier ordinal.
    injected = bytearray(data)
    for pc, value in ((0x800140AC, 117), (0x800140A0, 117 * 20), (0x800140B0, 117 * 20)):
        struct.pack_into('<I', injected, pc - BASE, (image.word(pc) & 0xffff0000) | value)
    struct.pack_into('<H', injected, replay['table'] + 117 * 20 - BASE, replay['rows'][0]['text_id'])
    controls.append(dict(name='replay earlier start plus matching caption', original_hits=hits(replay['ids']),
                         injected_hits=hits(replay_domain(Image(injected))['ids']), skipped=0))

    overlay_file = callers.prior.IMAGE.parent / 'TPW.OVL'
    packed = overlay_file.read_bytes()
    count = struct.unpack_from('<I', packed)[0]
    directory = [struct.unpack_from('<II', packed, 4 + 8 * i) for i in range(count)]
    overlays = []
    for index, (directory_word, off) in enumerate(directory):
        end = directory[index + 1][1] if index + 1 < count else len(packed)
        decoded, consumed = overlay_decode(packed[off:end])
        row = callers.scan(decoded, 0x80114158, targets)
        control = callers.scan(decoded, 0x80114158, [0x8006F00C])
        injected = (struct.pack('<I', 0x0c000000 | ((0x80017024 >> 2) & 0x3ffffff))
                    + decoded[4:-4] + struct.pack('<I', 0x80017024))
        injected_scan = callers.scan(injected, 0x80114158, [0x80017024])
        overlays.append(dict(index=index, packed_offset=off, directory_word=directory_word,
                             decoded_sha256=digest(decoded), consumed_packed_bytes=consumed,
                             unused_packed_tail=end-off-consumed, **row,
                             real_string_calls=control['calls'],
                             injected_vm_calls=injected_scan['calls'],
                             injected_vm_pointers=injected_scan['pointers']))
    boot = (callers.prior.IMAGE.parent / 'SLES_026.88').read_bytes()
    if boot[:8] != b'PS-X EXE':
        raise ValueError('unknown boot format')
    boot_base, boot_size = struct.unpack_from('<II', boot, 24)
    if len(boot) != 2048 + boot_size:
        raise ValueError('unaccounted boot bytes')
    boot_scan = callers.scan(boot[2048:], boot_base, targets)
    boot_control = callers.scan(boot[2048:], boot_base, [0x801E3A88])
    boot_injected = (struct.pack('<I', 0x0c000000 | ((callers.SUBMIT >> 2) & 0x3ffffff))
                     + boot[2052:-4] + struct.pack('<I', callers.SUBMIT))
    boot_positive = callers.scan(boot_injected, boot_base, targets)
    fixed = callers.classify(data, BASE, [r['site'] for r in main_scan['calls'] if r['target'] == callers.SETTER],
                             callers.FIXED_SOURCES, callers.DYNAMIC)
    fixed_injected = callers.classify(patch_word(data, 0x800622B8, 0x24050075), BASE,
                                     [r['site'] for r in fixed['rows']], callers.FIXED_SOURCES, callers.DYNAMIC)
    fixed_positive = callers.fixed_prank_hits(fixed_injected)
    callback_positive = callers.scan(data, BASE, [0x8008C2B4])['pointers']
    prior_overlay_rows = json.loads((ROOT / 'findings/pranks-audit.json').read_text())['overlays']
    overlay_hashes_agree = ([o['decoded_sha256'] for o in overlays]
                            == [o['sha256'] for o in prior_overlay_rows])
    direct_domains = {target: [r['site'] for r in main_scan['calls'] if r['target'] == target]
                      for target in (0x8001408C, 0x80017024, 0x80067F84)}
    expected_domains = {0x8001408C: [0x8003B950], 0x80017024: [0x80016994], 0x80067F84: [0x80067F4C]}
    if direct_domains != expected_domains:
        raise ValueError('domain callers changed; hand trace must be extended')
    if (len(paths) != 7 or {r['setter'] for r in paths} != set(callers.DYNAMIC)
            or any(r['prank_ids'] for r in paths) or hits(tutorial['ids'])
            or fixed['unreviewed'] or fixed['stale_manifest_sites'] or callers.fixed_prank_hits(fixed)
            or callers.missing_jump_sources(data, BASE, callers.FIXED_SOURCES)
            or vm['unreferenced_code_words'] or any(c['original_hits'] or not c['injected_hits'] for c in controls)
            or main_scan['pointers'] or not overlay_hashes_agree
            or fixed_positive != [dict(site=0x800622B4, address=0x800622B8, id=117)]
            or callback_positive != [dict(site=0x800F79E0, target=0x8008C2B4)]
            or any(o['calls'] or o['pointers'] or len(o['injected_vm_calls']) != 1
                   or len(o['injected_vm_pointers']) != 1 for o in overlays)
            or boot_scan['calls'] or boot_scan['pointers'] or not boot_control['calls']
            or len(boot_positive['calls']) != 1 or len(boot_positive['pointers']) != 1):
        raise ValueError('changed reachability evidence or failed positive control')
    regions = {name: dict(start=start, end=end, words=(end-start)//4, skipped_words=0,
                          sha256=digest(image.read(start, end-start)))
               for name, (start, end) in REGIONS.items()}
    return dict(conclusion='117/118/119 are unreachable through all seven computed paths and the fixed submission paths in this unmodified build, with build-generated saves and intact memory.',
                image_sha256=digest(data), archive_sha256=digest(archive),
                paths=paths, computed_paths_skipped=0, tutorial_switch=tutorial, replay=replay,
                vm=dict(schedule_entry=schedule_entry, schedule_offset=schedule_off,
                        schedule_bytes=len(schedule), schedule_sha256=digest(schedule),
                        code_entry=code_entry, code_offset=code_off,
                        code_bytes=len(code), code_sha256=digest(code), **vm),
                staff=staff, award_callers=award_calls, award_ids=award_ids,
                breakdown_sources=selector_sources, breakdown_ids=selector_ids,
                main=main_scan, targets=targets, instruction_regions=regions,
                fixed_sites=sum(r['kind'] == 'fixed' for r in fixed['rows']),
                fixed_literals=sum(len(r.get('sources', [])) for r in fixed['rows']), fixed_sites_skipped=0,
                controls=controls, controls_skipped=0, fixed_positive_control=fixed_positive,
                callback_positive_control=callback_positive,
                overlays=overlays, overlays_skipped=0, prior_overlay_hashes_agree=overlay_hashes_agree,
                overlay_archive_sha256=digest(packed),
                boot=dict(sha256=digest(boot), base=boot_base, header_bytes_excluded=2048,
                          **boot_scan, real_control_calls=boot_control['calls'],
                          injected_submit_calls=boot_positive['calls'], injected_submit_pointers=boot_positive['pointers']),
                exclusions=['Modified/malformed/foreign saves can supply arbitrary card kind and caption; the load routine does not validate the tutorial invariant.',
                            'Memory corruption, modified FOLIO/TPW images, synthesized indirect entry addresses and arbitrary aliased writes are outside this normal-execution proof.',
                            'No live emulator execution or comprehensive unrelated asset-to-executable analysis; all 12 named overlays and the boot payload were scanned.',
                            'No runtime prank actor, object, or cleaning identity is inferred from dead notification captions.'])


def main():
    result = audit()
    (ROOT / 'findings/prank-reachability-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    for path in result['paths']:
        print(f"setter {path['setter']:#010x}: IDs {path['ids']}; prank hits {path['prank_ids']}; skipped {path['skipped']}")
    print('Tutorial table entries:', len(result['tutorial_switch']['rows']), 'skipped: 0; IDs:', result['tutorial_switch']['ids'])
    print('VM programs:', len(result['vm']['programs']), 'code words:', result['vm']['code_words'],
          'unreferenced:', result['vm']['unreferenced_code_words'], 'skipped: 0')
    print('Main words:', result['main']['aligned_words'], 'skipped: 0; fixed sites:', result['fixed_sites'], 'skipped: 0')
    print('Overlay entries:', len(result['overlays']), 'words:', sum(o['aligned_words'] for o in result['overlays']),
          'skipped: 0; boot words:', result['boot']['aligned_words'], 'skipped: 0; boot header excluded: 2048 bytes')
    print('Positive domain controls:', len(result['controls']), 'passed; skipped: 0')
    print(result['conclusion'])


if __name__ == '__main__':
    main()
