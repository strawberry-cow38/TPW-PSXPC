using System;

namespace TPW.Sim
{
    /// <summary>The walker's waypoint pool (pathfinder.md §7.1, 0x800F7A38).
    ///
    /// ⭐ THE PATH A GUEST WALKS IS A LINKED LIST OF CORNERS, NOT A LIST OF TILES. Only direction
    /// CHANGES become waypoints, so a thirty-tile corridor is two entries. That is why the pool is only
    /// a thousand entries for a whole park of walkers, and it is why a walker interpolates between
    /// widely spaced points rather than stepping tile to tile.
    ///
    /// ⚠ ENTRIES ARE SHARED AND FINITE. One pool of 1000 serves every guest and every member of staff.
    /// A search that finds a route but cannot allocate a corner for it FAILS - see
    /// <see cref="Pathfinder"/> - so a park busy enough to exhaust this produces path failures that look
    /// like unreachable destinations.</summary>
    public sealed class WaypointPool
    {
        /// <summary>1000 entries (0x80093A1C: `slti 0x3E8`).</summary>
        public const int Capacity = 1000;

        /// <summary>The next-index value that marks the end of a chain: 11 bits all set.</summary>
        public const int EndOfChain = 0x7FF;

        /// <summary>What a walker's head index is when it holds no chain.</summary>
        public const int NoChain = -1;

        // The original packs each entry into four bytes: a u16 of (next:11, fracX:2, fracY:2, used:1)
        // and two signed tile bytes. Kept as three arrays rather than one packed word because the
        // packing is a storage detail, but the FIELD WIDTHS are not - see TileX/TileY below.
        readonly ushort[] _word = new ushort[Capacity];
        readonly sbyte[] _tileX = new sbyte[Capacity];
        readonly sbyte[] _tileY = new sbyte[Capacity];

        int _hint;
        int _free;

        public WaypointPool() => Reset();

        /// <summary>0x80093A1C: every entry free, hint at the start. ⚠ This is what the "build item put
        /// down" unpause calls, and it does NOT touch any walker's head index - see
        /// <see cref="Pathfinder.ResetAfterBuildItemPlaced"/>.</summary>
        public void Reset()
        {
            // ⚠ A DELIBERATE DEVIATION, AND THE ONLY ONE IN THIS FILE. The original just wipes the
            // pool; it does not clear any walker's head index, so after a wipe a walker still points at
            // an entry that is now free and whose `next` bits are whatever was left behind. Here the
            // wipe terminates every entry instead, so a dangling head reads as a ONE-ENTRY chain at the
            // origin rather than following free entries in a circle. The bug is preserved - the walker
            // still has a chain it should not have - but it degrades to a wrong destination instead of
            // a hang. See Pathfinder.ResetAfterBuildItemPlaced.
            for (int i = 0; i < Capacity; i++) _word[i] = EndOfChain;
            Array.Clear(_tileX, 0, Capacity);
            Array.Clear(_tileY, 0, Capacity);
            _hint = 0;
            _free = Capacity;
        }

        /// <summary>How many entries are unallocated.</summary>
        public int FreeCount => _free;

        /// <summary>Where the next allocation starts looking.</summary>
        public int Hint => _hint;

        public bool IsAllocated(int index) => (_word[index] & 0x8000) != 0;

        /// <summary>0x80093A70: the first free entry at or after the hint.
        ///
        /// ⚠ WHETHER THIS WRAPS IS NOT ESTABLISHED. The findings record "first free at or after the
        /// hint ... returns −1 when none is free", which is implemented here literally: the scan runs to
        /// the end of the pool and stops. If the original also wrapped, this returns −1 in a case where
        /// it would have succeeded - entries free below the hint while the tail is full. In practice
        /// <see cref="Free"/> pulls the hint back to the lowest freed index, which keeps it near the
        /// bottom, so the two readings are hard to tell apart; they are not the same code.</summary>
        public int Alloc()
        {
            for (int i = _hint; i < Capacity; i++)
            {
                if ((_word[i] & 0x8000) != 0) continue;
                _word[i] = (ushort)(0x8000 | EndOfChain);
                _hint = i + 1;
                _free--;
                return i;
            }
            return -1;
        }

        /// <summary>0x80093B04: one entry back, and the hint moves back to it if it was lower.</summary>
        public void Free(int index)
        {
            if (index < 0 || index >= Capacity) throw new ArgumentOutOfRangeException(nameof(index));
            if ((_word[index] & 0x8000) == 0) return;
            _word[index] &= 0x7FFF;
            _free++;
            if (index < _hint) _hint = index;
        }

        /// <summary>0x80093B54: free a whole chain by following `next` to the end.</summary>
        public void FreeChain(int head)
        {
            while (head >= 0 && head != EndOfChain)
            {
                int next = Next(head);
                Free(head);
                head = next;
            }
        }

        /// <summary>The next entry in the chain, or −1 at the end (0x80094114).</summary>
        public int Next(int index)
        {
            int n = _word[index] & 0x7FF;
            return n == EndOfChain ? -1 : n;
        }

        /// <summary>0x800ED688 / 0x800940DC. Pass −1 for "end of chain".</summary>
        public void SetNext(int index, int next)
        {
            int n = next < 0 ? EndOfChain : next;
            if (n > EndOfChain) throw new ArgumentOutOfRangeException(nameof(next));
            _word[index] = (ushort)((_word[index] & ~0x7FF) | n);
        }

        /// <summary>0x80093928: store a position, rounded to the nearest quarter tile.
        ///
        /// ⭐ THE ROUNDING IS WHY A TILE CENTRE SURVIVES A ROUND TRIP AND A GATE LANE DOES NOT. Adding
        /// 0x20 before the shift rounds to the nearest 64 units, so `tile&lt;&lt;8 | 0x80` comes back
        /// byte for byte, while a point offset by ±0x180 loses up to 32 units.</summary>
        public void Encode(int index, int x, int y)
        {
            int ax = x + 0x20, ay = y + 0x20;
            // ⚠ THE TILE IS A SIGNED BYTE, so a park more than 128 tiles across cannot be addressed by a
            // waypoint at all - the coordinate wraps negative. The eight shipped maps are 44x74, which is
            // presumably why nobody hit it. Same limit as the search node in Pathfinder.
            _tileX[index] = (sbyte)(ax >> 8);
            _tileY[index] = (sbyte)(ay >> 8);
            int fx = (ax >> 6) & 3, fy = (ay >> 6) & 3;
            _word[index] = (ushort)((_word[index] & ~0x7800) | (fx << 11) | (fy << 13));
        }

        /// <summary>0x800938C0: the stored position back in 8.8 world units.</summary>
        public (int X, int Y) Decode(int index)
        {
            int fx = (_word[index] >> 11) & 3, fy = (_word[index] >> 13) & 3;
            return ((_tileX[index] << 8) + fx * 64, (_tileY[index] << 8) + fy * 64);
        }

        /// <summary>The chain from a head index, nearest point first. A convenience for callers and
        /// tests; the original has no such walk.
        ///
        /// ⚠ IT REFUSES TO FOLLOW A CYCLE. A chain cannot loop while it is intact, but a head index
        /// left dangling by a pool wipe can point into anything, and the original would simply run off
        /// into it. Walking more than the pool holds is a corrupt chain by definition, so this throws
        /// rather than hanging - a test that regresses the wipe should go red, not go quiet.</summary>
        public System.Collections.Generic.List<(int X, int Y)> Chain(int head)
        {
            var points = new System.Collections.Generic.List<(int, int)>();
            for (int i = head, n = 0; i >= 0 && i != EndOfChain; i = Next(i))
            {
                if (++n > Capacity) throw new InvalidOperationException("waypoint chain does not terminate");
                points.Add(Decode(i));
            }
            return points;
        }

        /// <summary>How many entries a chain holds. Throws on a chain that does not terminate, for the
        /// reason given on <see cref="Chain"/>.</summary>
        public int ChainLength(int head)
        {
            int n = 0;
            for (int i = head; i >= 0 && i != EndOfChain; i = Next(i))
                if (++n > Capacity) throw new InvalidOperationException("waypoint chain does not terminate");
            return n;
        }
    }
}
