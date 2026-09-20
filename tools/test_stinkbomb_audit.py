#!/usr/bin/env python3
"""Tests for the SEARCH TOOL, not tests of an established stinkbomb simulation."""
import struct
import unittest
import audit_stinkbombs as audit


class AuditTests(unittest.TestCase):
    # REJECTS JAL-only censuses, lost high address bits, and dropping the last aligned word.
    def test_direct_calls_include_jumps_and_final_word(self):
        target = 0x800514E0
        low = (target >> 2) & 0x3FFFFFF
        data = struct.pack('<IIII', 0, 0x0C000000 | low, 0x08000000 | (low + 1), 0x08000000 | low)
        self.assertEqual([0x80010004, 0x8001000C], audit.direct_calls(data, target))
        self.assertEqual([], audit.direct_calls(data, target + 8))

    # REJECTS treating function pointers as direct calls, and ignoring descriptor callbacks.
    def test_literal_callback_is_distinct_from_a_call(self):
        data = struct.pack('<II', 0, 0x8008C2B4)
        self.assertEqual([0x80010004], audit.literals(data, 0x8008C2B4))
        self.assertEqual([], audit.direct_calls(data, 0x8008C2B4))

    # REJECTS missing ORI loads and calling an ADDIU with a nonzero source a literal id load.
    def test_immediate_scan_has_a_negative_control(self):
        data = struct.pack('<IIII', 0x24050075, 0x34060075, 0x24850075, 0x24050076)
        self.assertEqual([('0x80010000', 5), ('0x80010004', 6)],
                         [(r['site'], r['register']) for r in audit.immediate_loads(data, 117)])

    # REJECTS the old off+0x40 guard that excluded all short bins, and reading the type as the name.
    def test_short_record_uses_only_fields_actually_read(self):
        data = bytearray(0x28)
        struct.pack_into('<I', data, 0, 0x96)
        struct.pack_into('<I', data, 0x14, 0x20)
        struct.pack_into('<II', data, 0x20, 2, 337)
        kind, rec = audit.record(data)
        self.assertEqual('attraction', kind)
        self.assertEqual(337, rec['text_id'])
        self.assertTrue(rec['old_guard_would_skip'])
        self.assertEqual(('truncated', None), audit.record(data[:-1]))

    # REJECTS dropping type 8, counting type 150 as an attraction, and silently losing non-records.
    def test_record_exclusions_are_classified(self):
        data = bytearray(0x28)
        struct.pack_into('<I', data, 0, 0x96)
        struct.pack_into('<I', data, 0x14, 0x20)
        struct.pack_into('<II', data, 0x20, 8, 12)
        self.assertEqual('attraction', audit.record(data)[0])
        struct.pack_into('<I', data, 0x20, 150)
        self.assertEqual('other_type', audit.record(data)[0])
        data[0] = 0
        self.assertEqual(('non_record', None), audit.record(data))

    # REJECTS case-sensitive symbol searches and interpreting an unrelated string as a name hit.
    def test_raw_search_recovers_caption_and_symbol(self):
        data = b'Nothing relevant\0stink bomb\0STR_ADVMES_ADD_PRANK_SBOMB\0'
        self.assertEqual(['stink bomb', 'STR_ADVMES_ADD_PRANK_SBOMB'],
                         [r['text'] for r in audit.ascii_hits(data)])

    # REJECTS dropping the last string and confusing string ids with payload offsets.
    def test_text_offsets_and_last_entry(self):
        data = struct.pack('<III', 2, 12, 18) + b'First\0Last\0'
        self.assertEqual([dict(id=0, offset=12, text='First'), dict(id=1, offset=18, text='Last')],
                         audit.strings(data))

    # REJECTS counting Action8 (remove-message in binary) as PostMessage, and inventing a post.
    def test_rule_posts_have_a_nonposting_control(self):
        data = bytearray(54)
        struct.pack_into('<II', data, 16, 32, 12)
        struct.pack_into('<II', data, 24, 44, 10)
        struct.pack_into('<hhhhh', data, 44, 7, 117, 8, 118, 0)
        r = audit.rules(data)
        self.assertEqual([dict(rule=0, word_offset=0, message=117)], r['posts'])
        self.assertEqual((1, 2, 5, 0, 0), (r['programs'], r['instructions'], r['code_words'],
                                         r['unreferenced_code_words'], r['skipped_programs']))


if __name__ == '__main__':
    unittest.main()
