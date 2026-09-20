#!/usr/bin/env python3
"""Disc-backed falsifiers for ride_phases.py; no proprietary fixture files.

TPW_PHASE_ARCHIVE must name the source archive. Deliberately malformed fixtures
below are test inputs, never claims about game constants or valid game assets.
"""
import os
import struct
import unittest
from collections import Counter
from pathlib import Path

import ride_phases as phases


class RidePhaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = Path(os.environ['TPW_PHASE_ARCHIVE']).read_bytes()
        cls.entries = phases.archive_entries(cls.data)
        cls.rows = {r['folio']: r for r in phases.ride_rows(cls.data)}

    # REJECTS starting the directory at +4, wrong pair stride, or ignoring size.
    def test_archive_addresses_are_absolute_and_sizes_exact(self):
        self.assertEqual(422, len(self.entries))
        offset, entry = self.entries[220]
        self.assertEqual(0x3CE800, offset)
        self.assertEqual(len(entry), struct.unpack_from('<I', self.data, 0x3CE818)[0])
        self.assertEqual(41, struct.unpack_from('<H', self.data, 0x3CE838)[0])

    # REJECTS treating the first unpacked-size word as count, four logical slots
    # as four descriptors, or silently dropping any of the four ride classes.
    def test_all_ride_records_and_subentries_are_accounted_for(self):
        self.assertEqual(Counter({3: 59, 1: 12, 6: 8, 7: 4}),
                         Counter(r['type'] for r in self.rows.values()))
        definitions = [e for _, e in self.entries if len(e) >= 32
                       and struct.unpack_from('<I', e)[0] == 0x96
                       and struct.unpack_from('<I', e, 20)[0]
                       and 1 <= struct.unpack_from('<I', e, struct.unpack_from('<I', e, 20)[0])[0] <= 8]
        self.assertEqual(244, len(definitions))
        # recs.py's old 64-byte-tail guard excludes 47 short Feature records.
        self.assertEqual(197, sum(struct.unpack_from('<I', e, 20)[0] + 64 <= len(e)
                                  for e in definitions))
        for row in self.rows.values():
            if row['type'] == 3:
                self.assertEqual([-1, 0, -1, -1], row['phase_map'])
                self.assertEqual([0x38], row['mesh_offsets'])
        self.assertEqual([201, 5, 101, 101, 101, 101, 101, 101, 201, 101],
                         self.rows[41]['mesh_lengths'])

    # REJECTS reading mesh+0x38 (bounds), descriptor bytes from the file, or
    # substituting track count/key-end time for the header length.
    def test_named_lengths_and_definition_locations(self):
        for index, name, length, record in (
                (39, 'The Dizzy Tree', 239, 0x4060),
                (220, 'Crazy Ape', 41, 0x25C8),
                (379, 'Zero G', 801, 0x4F48)):
            row = self.rows[index]
            self.assertEqual((name, length, record),
                             (row['name'], row['logical_phase_1_length'], row['record_offset']))

    # REJECTS lhu being ported as lh or lw. A deliberately altered header makes
    # width observable; the real ride headers have zero high halfwords.
    def test_length_is_unsigned_and_only_the_low_halfword(self):
        entry = bytearray(self.entries[220][1])
        struct.pack_into('<I', entry, 0x38, 0xABCD8001)  # synthetic, not a disc length
        self.assertEqual([0x8001], phases.model_lengths(entry)[1])

    # REJECTS using logical phase directly as a sub-entry, unsigned -1, wrapping
    # the logical phase, and rejecting an absent logical phase instead of zero fallback.
    def test_map_selection_precedes_subentry_modulo(self):
        mapping, lengths, _ = phases.model_lengths(self.entries[41][1])
        self.assertEqual(201, phases.phase_length(mapping, lengths, 1))
        self.assertEqual(5, phases.phase_length(mapping, lengths, 3))
        for absent in (-1, 0, 2, 4, 7):
            self.assertEqual(201, phases.phase_length(mapping, lengths, absent))
        # Deliberately point beyond the ten meshes: the accessor wraps, unlike the map.
        mapping[1] = len(lengths) + 1
        self.assertEqual(5, phases.phase_length(mapping, lengths, 1))

    # REJECTS accepting packed data as a raw header, or reading beyond a mesh.
    def test_packed_and_truncated_meshes_fail_by_name(self):
        entry = bytearray(self.entries[220][1])
        struct.pack_into('<I', entry, 0x34, len(entry))
        with self.assertRaisesRegex(ValueError, 'compressed mesh'):
            phases.model_lengths(entry)
        with self.assertRaisesRegex(ValueError, 'truncated mesh header'):
            phases.model_lengths(self.entries[220][1][:0x38])
        empty = bytearray(self.entries[220][1])
        struct.pack_into('<I', empty, 4, 0)
        with self.assertRaisesRegex(ValueError, 'sub-entry directory'):
            phases.model_lengths(empty)

    # REJECTS treating truncated directory/entry bytes as a complete archive.
    def test_bad_archive_extents_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'directory'):
            phases.archive_entries(self.data[:8])
        referenced_end = max(offset + len(entry) for offset, entry in self.entries)
        with self.assertRaisesRegex(ValueError, 'entry'):
            phases.archive_entries(self.data[:referenced_end - 1])

    # REJECTS wrong 0x34 level stride, swapped min/max, rounding up half-max,
    # clamping to the minimum, assuming all levels equal, or fixed-1 Space Balls.
    def test_defaults_preserve_the_disc_oddities(self):
        self.assertEqual([1, 5, 1], self.rows[223]['default_cycles'])
        self.assertEqual([5, 5, 5], self.rows[374]['default_cycles'])
        self.assertEqual([2, 2, 2], self.rows[40]['default_cycles'])
        self.assertEqual([1, 1, 1], self.rows[55]['default_cycles'])
        self.assertEqual([30, 30, 30], self.rows[360]['cycles_min'])
        self.assertEqual([45, 50, 55], self.rows[360]['cycles_max'])
        self.assertEqual([22, 25, 27], self.rows[360]['default_cycles'])

    # REJECTS dropping -1, wrong fixed-point scale, floor in place of ceiling,
    # adding one at exact equality, ignoring the signed-positive cap, or L=1 -> 0 calls.
    def test_clock_thresholds_and_rounding_are_not_interchangeable(self):
        self.assertEqual(40, phases.phase_ticks(41, 4096))
        self.assertEqual(17, phases.phase_ticks(41))
        self.assertEqual(98, phases.phase_ticks(239))
        self.assertEqual(329, phases.phase_ticks(801))
        self.assertEqual(10, phases.phase_ticks(41, 0x8000))
        self.assertEqual(1, phases.phase_ticks(1))
        with self.assertRaises(ValueError):
            phases.phase_ticks(0)
        with self.assertRaises(ValueError):
            phases.phase_ticks(41, 0)

    # REJECTS retaining phase overshoot by rounding the whole run just once;
    # 5 * ceil(163840/9984) = 85, whereas ceil(5*163840/9984) = 83.
    def test_each_phase_discards_overshoot_before_the_next(self):
        self.assertEqual([85, 85, 85], self.rows[220]['reference_run_ticks'])
        self.assertEqual([490, 490, 490], self.rows[39]['reference_run_ticks'])
        self.assertEqual([1645, 1645, 1645], self.rows[379]['reference_run_ticks'])
        self.assertEqual([5, 5, 5], self.rows[135]['reference_run_ticks'])

    # REJECTS presenting the inherited flat-ride calculation as a tour/track/coaster
    # trip duration, and taking the tour cache from logical map slot 1 (mesh 0).
    def test_other_classes_keep_lengths_without_inventing_trip_times(self):
        for row in self.rows.values():
            if row['type'] != 3:
                self.assertIsNone(row['reference_phase_ticks'])
                self.assertIsNone(row['reference_run_ticks'])
        self.assertEqual([51, 51, 48, 101],
                         [self.rows[i]['mesh_lengths'][1] for i in (56, 142, 208, 372)])

    # REJECTS a hand-edited or stale published table; a regeneration must be byte
    # identical. Independent pinned rows above keep this from being the sole oracle.
    def test_published_table_is_reproducible_and_units_are_seconds(self):
        generated = phases.markdown(list(self.rows.values()))
        self.assertIn('| 5/5/5 | 3.40/3.40/3.40 |', generated)
        self.assertEqual((Path(__file__).resolve().parents[1] /
                          'findings/ride-phase-lengths.md').read_text(), generated)


if __name__ == '__main__':
    unittest.main(verbosity=2)
