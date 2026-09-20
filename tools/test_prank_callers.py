#!/usr/bin/env python3
"""Tests of the bounded search, NOT of an established prank simulation."""
import struct
import unittest

import audit_prank_callers as audit


def packed(*words):
    return struct.pack('<' + 'I' * len(words), *words)


class CallerAuditTests(unittest.TestCase):
    # REJECTS JAL-only/J-only searches, mismatched targets, and dropping the final word.
    def test_calls_and_tail_jumps_with_nonmatching_control(self):
        target = 0x80014144
        low = (target >> 2) & 0x03FFFFFF
        data = packed(0x0C000000 | low, 0x0C000000 | (low + 1), 0x08000000 | low)
        result = audit.scan(data, 0x80010000, (target,))
        self.assertEqual([(0x80010000, 'JAL'), (0x80010008, 'J')],
                         [(c['site'], c['kind']) for c in result['calls']])
        self.assertEqual([], audit.scan(data, 0x80010000, (target + 8,))['calls'])

    # REJECTS hardcoding TPW.BIN's base and taking the jump region from PC instead of PC+4.
    def test_base_and_jump_region(self):
        self.assertEqual([dict(site=0x8FFFFFFC, target=0x90000000, kind='J')],
                         audit.scan(packed(0x08000000), 0x8FFFFFFC, (0x90000000,))['calls'])
        target = 0x80014144
        call = packed(0x0C000000 | ((target >> 2) & 0x03FFFFFF))
        self.assertEqual([dict(site=0x80114158, target=target, kind='JAL')],
                         audit.scan(call, 0x80114158, (target,))['calls'])

    # REJECTS treating a callback pointer as a call, or silently ignoring pointers.
    def test_pointer_control(self):
        result = audit.scan(packed(0, 0x8008C2B4), 0x80010000, (0x8008C2B4,))
        self.assertEqual([], result['calls'])
        self.assertEqual([dict(site=0x80010004, target=0x8008C2B4)], result['pointers'])

    # REJECTS rounding up words and hiding undecoded trailing bytes in the skip count.
    def test_partial_word_accounting(self):
        result = audit.scan(packed(0) + b'\x01\x02\x03', 0x80010000, ())
        self.assertEqual((7, 1, 0, 3), (result['bytes'], result['aligned_words'],
                                      result['skipped_aligned_words'], result['trailing_bytes_not_decoded']))
        self.assertEqual(0, audit.scan(b'', 0x80010000, ())['aligned_words'])

    # REJECTS reading the wrong argument register, nonliteral arithmetic, or omitting ORI.
    def test_literal_argument_has_negative_controls(self):
        self.assertEqual(117, audit.literal_a1(0x24050075))
        self.assertEqual(118, audit.literal_a1(0x34050076))
        self.assertEqual(65535, audit.literal_a1(0x2405FFFF))
        self.assertIsNone(audit.literal_a1(0x24070075))
        self.assertIsNone(audit.literal_a1(0x24A50075))
        self.assertIsNone(audit.literal_a1(0x8C050075))

    # REJECTS dropping alternate branch sources or delay-slot sources; silence alone cannot pass.
    def test_fixed_branch_sources_and_prank_control(self):
        base = 0x80010000
        data = packed(0x24050078, 0x24050075, 0x34050076, 0x24050077)
        try:
            result = audit.classify(data, base, [base], {base: (base, base+4, base+8, base+12)}, {})
        except ValueError as exc:
            self.fail('Valid literal sources were rejected: ' + str(exc))
        self.assertEqual([120, 117, 118, 119], [s['id'] for s in result['rows'][0]['sources']])
        self.assertEqual([117, 118, 119], [s['id'] for s in audit.fixed_prank_hits(result)])
        control = audit.classify(data, base, [base], {base: (base,)}, {})
        self.assertEqual([], audit.fixed_prank_hits(control))

    # REJECTS reporting an unknown caller as reviewed, discarding stale entries, or inventing computed ids.
    def test_computed_and_missing_sites_stay_explicit(self):
        result = audit.classify(b'', 0x80010000, [10, 20], {}, {10: 'unknown domain', 30: 'stale'})
        self.assertEqual(['computed', 'UNREVIEWED'], [r['kind'] for r in result['rows']])
        self.assertEqual(1, result['unreviewed'])
        self.assertEqual([30], result['stale_manifest_sites'])
        self.assertEqual([], audit.fixed_prank_hits(result))

    # REJECTS a changed instruction becoming an invented zero id or a negative no-prank finding.
    def test_invalid_literal_source_fails_closed(self):
        base = 0x80010000
        with self.assertRaisesRegex(ValueError, 'no longer a literal'):
            audit.classify(packed(0x8C050075), base, [base], {base: (base,)}, {})
        with self.assertRaisesRegex(ValueError, 'empty fixed'):
            audit.classify(packed(0), base, [base], {base: ()}, {})

    # REJECTS Python negative indexing, partial/unaligned reads, and contradictory classifications.
    def test_manifest_address_and_partition_validation(self):
        base = 0x80010000
        for source in (base-4, base+1, base+4):
            with self.subTest(source=source):
                with self.assertRaisesRegex(ValueError, 'outside aligned'):
                    audit.classify(packed(0x24050075), base, [base], {base: (source,)}, {})
        with self.assertRaisesRegex(ValueError, 'overlapping'):
            audit.classify(b'', base, [base], {base: ()}, {base: 'computed'})

    # REJECTS claiming a complete manifest while omitting an incoming J's literal delay slot.
    def test_independent_jump_source_guard(self):
        base = 0x80010000
        target = base + 16
        jump = 0x08000000 | ((target >> 2) & 0x03FFFFFF)
        data = packed(jump, 0x24050075, jump | 0x04000000, 0x24050076, 0)
        self.assertEqual([dict(jump=base, setter=target, source=base+4, id=117)],
                         audit.missing_jump_sources(data, base, {target: ()}))
        self.assertEqual([], audit.missing_jump_sources(data, base, {target: (base+4,)}))
        self.assertEqual([], audit.missing_jump_sources(packed(jump, 0), base, {target: ()}))
        self.assertEqual([], audit.missing_jump_sources(packed(jump), base, {target: ()}))


if __name__ == '__main__':
    unittest.main()
