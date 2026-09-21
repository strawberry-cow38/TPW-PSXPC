#!/usr/bin/env python3
"""Evidence-reader tests; these do not invent a pleasant/scenery producer."""
import struct
import unittest

import audit_bit1 as audit


def packed(*values):
    return struct.pack('<'+'I'*len(values), *values)


def mem(op, rs, rt, offset):
    return op << 26 | rs << 21 | rt << 16 | (offset & 65535)


class Bit1AuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = audit.IMAGE.read_bytes()

    def valid(self, fn, *args):
        try:
            return fn(*args)
        except (ValueError, IndexError, TypeError, struct.error) as exc:
            self.fail('Rejected valid evidence: '+str(exc))

    # REJECTS Python negative indexing, unaligned/partial reads, and hidden signed offsets.
    def test_checked_words_and_signed_immediates(self):
        self.assertEqual(0x12345678, audit.word(packed(0x12345678), 0x1000, 0x1000))
        for pc in (0xFFC, 0x1001, 0x1004):
            with self.assertRaisesRegex(ValueError, 'outside aligned'):
                audit.word(packed(0), 0x1000, pc)
        self.assertEqual([-32768, -1, 32767], [audit.signed(x) for x in (32768, 65535, 32767)])

    # REJECTS excluding J, branch-linked or interior entries, and decoding J with PC rather than PC+4.
    def test_direct_and_branch_edges(self):
        base, target = 0x80010000, 0x80010020
        low = target >> 2 & 0x3FFFFFF
        data = packed(0x0C000000 | low, 0x08000000 | low, 0x10000005, 0x04110004, 0)
        rows = audit.scan(data, base, {target})['edges']
        self.assertEqual(['JAL', 'J', 'branch', 'branch'], [r['kind'] for r in rows])
        self.assertEqual([base, base+4, base+8, base+12], [r['site'] for r in rows])
        self.assertEqual([], audit.scan(data, base, {target+4})['edges'])
        self.assertEqual([dict(site=0x8FFFFFFC, target=0x90000000, kind='J')],
                         audit.scan(packed(0x08000000), 0x8FFFFFFC, {0x90000000})['edges'])

    # REJECTS loss of the final aligned pointer and hiding trailing bytes as examined instructions.
    def test_literal_pointer_and_tail_accounting(self):
        target = 0x800961D8
        row = audit.scan(packed(0, target)+b'abc', 0x80010000, {target})
        self.assertEqual([dict(site=0x80010004, target=target)], row['pointers'])
        self.assertEqual((11, 2, 0, 3), (row['bytes'], row['aligned_words'], row['skipped_words'], row['trailing_bytes']))
        self.assertEqual([], row['edges'])

    # REJECTS the old stack/GP exclusions and SW-only searches; every fixture population is nonempty.
    def test_every_store_opcode_and_base(self):
        ops = (40, 41, 42, 43, 46, 56, 57, 58, 59)
        data = packed(*(mem(op, rs, 5, 20) for op in ops for rs in (4, 28, 29)))
        row = audit.scan(data, 0x1000, set())
        self.assertEqual(27, len(row['field_store_candidates']))
        self.assertEqual(9, len(row['store_opcode_counts']))
        self.assertEqual({4, 28, 29}, {r['base_register'] for r in row['field_store_candidates']})
        self.assertEqual([], audit.scan(packed(mem(35, 4, 5, 20)), 0x1000, set())['field_store_candidates'])

    # REJECTS ignoring upper-byte stores or treating adjacent geometry/unknown words as flags.
    def test_byte_half_and_partial_overlap(self):
        data = packed(*(mem(op, 4, 5, off) for op, off in
                        ((40, 20), (40, 23), (41, 22), (42, 23), (46, 21), (43, 16), (43, 24))))
        self.assertEqual(5, len(audit.scan(data, 0x1000, set())['field_store_candidates']))
        self.assertEqual([20, 21, 22], audit.store_bytes(42, 22))
        self.assertEqual([21, 22, 23], audit.store_bytes(46, 21))
        self.assertEqual([22, 23], audit.store_bytes(41, 22))
        with self.assertRaisesRegex(ValueError, 'not a store'):
            audit.store_bytes(35, 20)

    # REJECTS an unsigned negative offset falsely overlapping entry+0x14.
    def test_signed_store_offset(self):
        row = audit.scan(packed(mem(43, 4, 5, -20)), 0x1000, set())
        self.assertEqual({'sw': 1}, row['store_opcode_counts'])
        self.assertEqual([], row['field_store_candidates'])

    # REJECTS treating any same-offset store as an identical leaf, or dropping a last-word delay slot.
    def test_clone_search_has_nonmatching_controls(self):
        data = packed(0x03E00008, 0xAC850014, 0x03E00008, 0xAC850018, 0x03E00008, 0xAC850014)
        self.assertEqual([0x1000, 0x1010], audit.scan(data, 0x1000, set())['identical_setters'])

    # REJECTS omitting GP address formation while confusing another register's immediate with a global.
    def test_pool_root_references(self):
        data = packed(mem(35, 28, 4, 0x120C), mem(43, 28, 2, 0x120C),
                      mem(9, 28, 8, 0x120C), mem(35, 4, 4, 0x120C))
        self.assertEqual([0x1000, 0x1004, 0x1008],
                         [r['site'] for r in audit.scan(data, 0x1000, set())['pool_gp_references']])

    # REJECTS missing high/low pointer construction, interior function addresses, or a copied GP base.
    def test_absolute_and_gp_alias_references(self):
        data = packed(0x3C088010, 0x25083860, 0x8D040000, 0x03804821, 0x8D24120C,
                      0x3C0A8009, 0x354A61DC)
        result = audit.constant_refs(data, 0x1000, {audit.POOL, 0x800961DC})
        self.assertEqual([0x1004, 0x1008, 0x1010, 0x1018], [r['site'] for r in result])
        self.assertEqual([audit.POOL]*3+[0x800961DC], [r['value'] for r in result])

    # REJECTS retaining stale high halves after loads, calls or arithmetic the checker does not model.
    def test_constant_invalidation_and_delay(self):
        for middle in (0x8C880000, 0x00084080):
            data = packed(0x3C088010, middle, 0x25083860)
            self.assertEqual([], audit.constant_refs(data, 0x1000, {audit.POOL}))
        data = packed(0x3C088010, 0x0C000000, 0x25093860, 0x250A3860)
        self.assertEqual([0x1008], [r['site'] for r in audit.constant_refs(data, 0x1000, {audit.POOL})])

    # REJECTS source-register mistakes, unsigned ADDIU, and assuming a computed flag is zero.
    def test_literal_domain(self):
        self.assertEqual([1, 65535, 0xFFFFFFFF], [audit.literal_a1(w) for w in (0x24050001, 0x3405FFFF, 0x2405FFFF)])
        for w in (0x24040001, 0x24A50001, 0x8C850014):
            with self.assertRaisesRegex(ValueError, 'not literal a1'):
                audit.literal_a1(w)

    # REJECTS missing the JR delay-slot store and ORing rather than replacing the flag word.
    def test_leaf_whole_word_write(self):
        rows = self.valid(audit.leaf_writes, packed(0x03E00008, 0xAC850014), 0x1000, 0x1000, 0x1008, 2)
        self.assertEqual(1, len(rows))
        self.assertEqual((0x1004, [20, 21, 22, 23], 2, True),
                         (rows[0]['site'], rows[0]['bytes'], rows[0]['value'], rows[0]['flag_overlap']))

    # REJECTS offset-only searches: a new writer stores at alias+4 after entry+16 arithmetic.
    def test_new_aliased_writer(self):
        data = packed(0x24880010, 0x24050001, 0x03E00008, 0xAD050004)
        self.assertEqual([], audit.scan(data, 0x1000, set())['field_store_candidates'])
        rows = self.valid(audit.leaf_writes, data, 0x1000, 0x1000, 0x1010)
        self.assertEqual((1, [20, 21, 22, 23], True), (rows[0]['value'], rows[0]['bytes'], rows[0]['flag_overlap']))

    # REJECTS losing register-copy aliases, negative displacements and single-byte writes.
    def test_copied_alias_and_negative_displacement(self):
        data = packed(0x00804021, 0x25080018, 0x24050001, 0x03E00008, mem(40, 8, 5, -4))
        rows = self.valid(audit.leaf_writes, data, 0x1000, 0x1000, 0x1014)
        self.assertEqual(([20], 1, True), (rows[0]['bytes'], rows[0]['value'], rows[0]['flag_overlap']))

    # REJECTS inventing zero stores for unknown instructions/destinations and vacuous empty leaf passes.
    def test_leaf_unknowns_fail_closed(self):
        cases = ((packed(0xAC850014), 4, 'missing final'),
                 (packed(0x8C840000, 0x03E00008, 0xAC850014), 12, 'unbounded'),
                 (packed(0x0C000000, 0x03E00008, 0), 12, 'unsupported'),
                 (b'', 0, 'empty leaf'))
        for data, size, reason in cases:
            with self.assertRaisesRegex(ValueError, reason):
                audit.leaf_writes(data, 0x1000, 0x1000, 0x1000+size)

    # REJECTS classifying an unknown call/branch as reviewed, stale manifests, and empty-domain completeness.
    def test_ingress_partition(self):
        rows = [dict(site=0x1004, target=0x1000), dict(site=0x2000, target=0x1000), dict(site=0x3000, target=0x1004)]
        result = audit.partition_ingress(rows, {'leaf': (0x1000, 0x1010)}, {0x2000: 0x1000, 0x4000: 0x1000})
        self.assertEqual(1, result['internal_count'])
        self.assertEqual([rows[2]], result['unexpected'])
        self.assertEqual([dict(site=0x4000, target=0x1000)], result['missing'])
        for regions, expected in (({}, {1: 2}), ({'leaf': (1, 2)}, {})):
            with self.assertRaisesRegex(ValueError, 'empty ingress'):
                audit.partition_ingress([], regions, expected)

    # REJECTS losing one producer, delay-slot constants or bit-1 detection; two nonempty injected domains.
    def test_real_producer_domains_and_injections(self):
        rows = self.valid(audit.flag_domain, self.data)
        self.assertEqual([4, 2], [r['flags'] for r in rows])
        self.assertEqual([False, False], [r['bit1'] for r in rows])
        for source in (0x8008C330, 0x80095A38):
            rows = self.valid(audit.flag_domain, audit.patched(self.data, audit.BASE, source, 0x24050001))
            self.assertEqual(1, sum(r['bit1'] for r in rows))

    # REJECTS a changed call or missing/misdirected payload store silently preserving a negative result.
    def test_changed_producer_rejected(self):
        for address, ins in ((0x80095A34, 0), (0x800961DC, 0xAC850018)):
            with self.assertRaisesRegex(ValueError, 'changed flag'):
                audit.flag_domain(audit.patched(self.data, audit.BASE, address, ins))

    # REJECTS missing owner partial stores, concealing stack exclusions, and empty owner populations.
    def test_owner_access_accounting(self):
        data = packed(mem(43, 16, 2, 76), mem(35, 16, 4, 76), mem(40, 16, 5, 79),
                      mem(42, 16, 5, 79), mem(43, 29, 5, 76), mem(43, 16, 2, 80))
        row = audit.owner_accesses(data, 0x1000, 0x1000, 0x1018, (76,))
        self.assertEqual(4, len(row['accesses']))
        self.assertEqual(1, len(row['stack_accesses_excluded']))
        self.assertEqual((6, 0), (row['words'], row['skipped_words']))
        for end, offsets in ((0x1000, (76,)), (0x1018, ())):
            with self.assertRaisesRegex(ValueError, 'empty owner'):
                audit.owner_accesses(data, 0x1000, 0x1000, end, offsets)

    # REJECTS a zero-result inherited-field scan without a nonempty injected control.
    def test_real_inherited_owner_negative_with_control(self):
        args = audit.OWNER_WINDOWS['person_staff_plus8']
        row = audit.owner_accesses(self.data, audit.BASE, *args)
        self.assertEqual((1923, []), (row['words'], row['accesses']))
        injected = audit.patched(self.data, audit.BASE, args[0], 0xAC850044)
        self.assertEqual(1, len(audit.owner_accesses(injected, audit.BASE, *args)['accesses']))

    # REJECTS vacuous scan controls and omitting the actual extra flag store or interior target.
    def test_injection_controls_require_every_detection(self):
        row = audit.injection_control(bytes(24), 0x80010000)
        self.assertEqual((5, 6, 0, True), (len(row['checks']), row['injections'], row['skipped'], row['passed']))
        self.assertTrue(all(row['checks'].values()))
        with self.assertRaisesRegex(ValueError, 'empty control'):
            audit.injection_control(bytes(20), 0x80010000)

    # REJECTS helper-code-range exclusions and an incomplete real ingress/field-store census.
    def test_real_full_image_census(self):
        row = audit.scan(self.data, audit.BASE, audit.targets())
        self.assertEqual((266327, 0, 0), (row['aligned_words'], row['skipped_words'], row['trailing_bytes']))
        self.assertEqual(3239, len(row['field_store_candidates']))
        self.assertEqual(9, len(row['identical_setters']))
        partition = audit.partition_ingress(row['edges'], audit.REGIONS, audit.INGRESS)
        self.assertEqual((15, [], []), (len(partition['external']), partition['unexpected'], partition['missing']))
        self.assertEqual(list(audit.POOL_REFS), [r['site'] for r in row['pool_gp_references']])

    # REJECTS skipped executable populations, empty controls, and losing the two real callback pointers.
    def test_complete_audit_populations(self):
        result = self.valid(audit.audit)
        self.assertEqual(12, len(result['overlays']))
        self.assertEqual(30634, sum(r['aligned_words'] for r in result['overlays']))
        self.assertEqual(11776, result['boot']['aligned_words'])
        self.assertEqual(5, len(result['controls']))
        self.assertTrue(all(r['passed'] for r in result['controls']))
        self.assertEqual(2, len(result['extra_target_census']['pointers']))
        self.assertEqual(3, len(result['extra_target_census']['edges']))
        for row in result['overlays']+[result['boot']]:
            self.assertEqual(([], [], [], 0), (row['edges'], row['pointers'], row['constant_references'], row['skipped_words']))
            self.assertEqual((True, 5, 0), (row['injection']['passed'], len(row['injection']['checks']), row['injection']['skipped']))
        self.assertEqual(5, len(result['payload_leaf_writes']))
        self.assertEqual(6, len(result['owner_windows']))


if __name__ == '__main__':
    unittest.main()
