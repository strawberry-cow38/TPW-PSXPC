#!/usr/bin/env python3
"""Reject false computed-ID negatives; these test the audit, not prank gameplay."""
import struct
import unittest

import audit_prank_reachability as audit


def schedule(*starts):
    return b''.join(struct.pack('<h10x', start) for start in starts)


def words(*values):
    return struct.pack('<' + 'h' * len(values), *values)


class ReachabilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original = audit.callers.prior.IMAGE.read_bytes()

    def valid(self, function, *args):
        try:
            return function(*args)
        except (ValueError, IndexError, struct.error) as exc:
            self.fail('Rejected valid evidence: ' + str(exc))

    # REJECTS negative Python slices, partial reads, misalignment, and a changed opcode/register.
    def test_checked_image_and_signed_immediates(self):
        image = audit.Image(struct.pack('<II', 0x24A5FFFF, 0x80008000), 0x1000)
        self.assertEqual(-1, image.imm(0x1000, 9, 5, 5))
        self.assertEqual(65535, image.imm(0x1000, 9, 5, 5, False))
        self.assertEqual(32768, image.half(0x1004))
        for address, size in ((0xffc, 4), (0x1006, 4)):
            with self.assertRaisesRegex(ValueError, 'outside'):
                image.read(address, size)
        with self.assertRaisesRegex(ValueError, 'unaligned'):
            image.word(0x1001)
        with self.assertRaisesRegex(ValueError, 'changed'):
            image.imm(0x1000, 9, 0, 5)

    # REJECTS treating 0x800DBE10 as IDs, omitting its final entry or losing J delay literals.
    def test_all_fourteen_tutorial_targets_and_default(self):
        result = self.valid(audit.tutorial_switch, audit.Image(self.original))
        self.assertEqual(0x800DBE10, result['address'])
        self.assertEqual([221, 222, 290, 290, 223, 224, 290, 290, 225, 226, 290, 290, 227, 228],
                         [row['id'] for row in result['rows']])
        self.assertEqual(290, result['outside_unsigned_domain_id'])
        self.assertEqual(0, result['skipped_entries'])
        injected = audit.patch_word(self.original, 0x800178E4, 0x24050075)
        self.assertEqual([117], audit.hits(self.valid(audit.tutorial_switch, audit.Image(injected))['ids']))

    # REJECTS silently converting unknown code, nonliteral delay setup, or a loop to default 290.
    def test_switch_unknowns_fail_closed(self):
        base, setter, submit = 0x1000, 0x1010, 0x1014
        jump = 0x08000000 | (base >> 2)
        for values, reason in (((0x8C850000, 0), 'unknown switch arm'),
                               ((jump, 0x8C850000), 'unknown switch delay'),
                               ((jump, 0), 'loop')):
            with self.subTest(reason=reason):
                image = audit.Image(struct.pack('<II', *values), base)
                with self.assertRaisesRegex(ValueError, reason):
                    audit.literal_arm(image, base, setter, submit, 290)

    # REJECTS last-row omission, last-match lookup, and treating text IDs as message IDs.
    def test_first_caption_exact_ordinal(self):
        self.assertEqual(221, self.valid(audit.first_caption, [44, 45, 44, 303], 44, 221))
        self.assertEqual(224, self.valid(audit.first_caption, [44, 45, 44, 303], 303, 221))
        self.assertEqual(117, self.valid(audit.first_caption, [44], 44, 117))
        self.assertEqual([], audit.hits([221, 224]))
        self.assertEqual([117, 118, 119], audit.hits([116, 117, 118, 119, 120]))

    # REJECTS converting an unbounded/malformed caption input into a reassuring empty result.
    def test_missing_caption_is_not_a_negative(self):
        with self.assertRaisesRegex(ValueError, 'termination witness'):
            audit.first_caption([44, 45], 864, 221)
        with self.assertRaisesRegex(ValueError, 'termination witness'):
            audit.first_caption([], 44, 221)

    # REJECTS counting 28 captionless rows as generated cards or losing the last tutorial card.
    def test_replay_actual_domain_and_suppressed_rows(self):
        result = self.valid(audit.replay_domain, audit.Image(self.original))
        self.assertEqual((221, 68, 68, 28, 40, 41),
                         (result['kind3_low'], result['kind3_count'], len(result['rows']),
                          result['captionless_rows'], len(result['ids']), len(result['conservative_ids'])))
        self.assertEqual((221, 288), (min(result['ids']), max(result['ids'])))
        self.assertNotIn(237, result['ids'])
        self.assertIn(237, result['conservative_ids'])
        self.assertEqual([], audit.hits(result['ids']))

    # REJECTS trusting a replay ordinal that no longer corresponds to its loaded row.
    def test_replay_mismatched_domain_is_rejected(self):
        injected = audit.patch_word(self.original, 0x800140AC, 0x24100075)
        with self.assertRaisesRegex(ValueError, 'row/index mismatch'):
            audit.replay_domain(audit.Image(injected))

    # REJECTS dropping a second schedule entry, treating Action8 as Post, or missing any prank ID.
    def test_vm_posts_and_action_control(self):
        code = words(7, 117, 8, 118, 0, 7, 118, 7, 119, 0)
        result = self.valid(audit.decode_programs, schedule(0, 5), code)
        self.assertEqual([117, 118, 119], result['ids'])
        self.assertEqual([(0, 0, 117), (1, 5, 118), (1, 7, 119)],
                         [(p['rule'], p['word'], p['id']) for p in result['posts']])
        self.assertEqual((10, 0, 0), (result['code_words'], result['unreferenced_code_words'], result['skipped_programs']))
        result = self.valid(audit.decode_programs, schedule(0), words(8, 117, 0))
        self.assertEqual([], result['ids'])

    # REJECTS widening signed operands without applying the setter's halfword mask.
    def test_vm_operand_wrap(self):
        result = self.valid(audit.decode_programs, schedule(0), words(7, -1, 0))
        self.assertEqual([65535], result['ids'])

    # REJECTS claiming total code coverage while dropping trailing unscheduled words.
    def test_vm_unreferenced_words_counted(self):
        result = self.valid(audit.decode_programs, schedule(0), words(7, 120, 0, 7, 117, 0))
        self.assertEqual([120], result['ids'])
        self.assertEqual(3, result['unreferenced_code_words'])

    # REJECTS discarding VM stores or their backing-counter aliases; checks the highest safe slot.
    def test_vm_writes_cannot_rewrite_schedule(self):
        result = self.valid(audit.decode_programs, schedule(0), words(5, 71, 1, 6, 70, 0, 0))
        self.assertEqual([[142], [140, 182]], [w['object_offsets'] for w in result['writes']])
        with self.assertRaisesRegex(ValueError, 'scheduler state'):
            audit.decode_programs(schedule(0), words(6, 71, 0, 0))

    # REJECTS negative starts, unknown/truncated instructions, partial records and indexed overrun.
    def test_vm_malformed_inputs_are_not_silence(self):
        for sched, code, reason in ((schedule(-1), words(0), 'outside'),
                                    (schedule(1), words(0), 'outside'),
                                    (b'\0', words(0), 'partial'),
                                    (schedule(0), b'\0', 'partial'),
                                    (schedule(0), words(10, 0), 'opcode'),
                                    (schedule(0), words(7), 'opcode'),
                                    (schedule(0), words(1, 72, 0, 0), 'outside cache')):
            with self.subTest(reason=reason):
                with self.assertRaisesRegex(ValueError, reason):
                    audit.decode_programs(sched, code)

    # REJECTS replacing the signed table with an assumed 0..4 range or omitting switch alternatives.
    def test_staff_signed_byte_and_all_offsets(self):
        self.assertEqual([49, 50, 54, 65458], audit.staff_ids([255, 0, 4, 128], [50]))
        self.assertEqual([30, 34, 35, 39, 40, 44, 50, 54], audit.staff_ids([0, 4], [30, 35, 40, 50]))
        self.assertEqual([117], audit.hits(audit.staff_ids([67], [50])))

    # REJECTS reading fewer than five permutation bytes or flattening the six actual switch arms.
    def test_actual_staff_tables(self):
        result = self.valid(audit.staff_domain, audit.Image(self.original))
        self.assertEqual([3, 0, 2, 4, 1], result['permutation'])
        self.assertEqual([1, 2, 3, 4, 5], result['staff_kinds'])
        self.assertEqual([30, 35, 40, 40, 40, 50], result['offsets'])
        self.assertEqual(list(range(30, 45)) + list(range(50, 55)), result['paths'][str(0x8006813C)])
        self.assertEqual(list(range(50, 55)), result['paths'][str(0x80067F28)])
        self.assertEqual(list(range(45, 50)), result['paths'][str(0x8006819C)])

    # REJECTS trusting an archive entry past EOF or silently accepting a partial directory.
    def test_archive_extents(self):
        archive = bytes(8) + struct.pack('<II', 16, 2) + b'OK'
        self.assertEqual((16, b'OK'), audit.archive_entry(archive, 0))
        with self.assertRaisesRegex(ValueError, 'directory'):
            audit.archive_entry(b'', 0)
        with self.assertRaisesRegex(ValueError, 'entry'):
            audit.archive_entry(archive[:-1], 0)

    # REJECTS reversed flag polarity and treating a terminator as a backward copy.
    def test_overlay_literal_and_terminator(self):
        self.assertEqual((b'ABC', 6), self.valid(audit.overlay_decode, b'\x08ABC\x00\x00TRAIL'))

    # REJECTS non-overlapping copies, wrong short-token distance, and wrong extended lengths.
    def test_overlay_all_back_reference_forms(self):
        # literal A, short distance-1 length-2, ordinary distance-1 length-3,
        # extended distance-1 length-(2+8), then terminator.
        self.assertEqual((b'A' * 16, 10), self.valid(audit.overlay_decode,
                         b'\x1eA\xff\x00\x01\x50\x01\x02\x00\x00'))
        # Unlike repeated A, this distinguishes distance 2 from distance 1.
        self.assertEqual((b'ABAB', 6), self.valid(audit.overlay_decode, b'\x0cAB\xfe\x00\x00'))

    # REJECTS consuming a neighboring packed entry to hide truncation or invalid copy distances.
    def test_overlay_bad_extents_fail(self):
        with self.assertRaisesRegex(ValueError, 'truncated'):
            audit.overlay_decode(b'\x00')
        try:
            audit.overlay_decode(b'\x01\xff')
        except (ValueError, IndexError) as exc:
            self.assertIsInstance(exc, ValueError, 'Invalid distance must fail explicitly, not index the output')
            self.assertIn('back reference', str(exc))
        else:
            self.fail('Accepted a backward reference before any output')


if __name__ == '__main__':
    unittest.main()
