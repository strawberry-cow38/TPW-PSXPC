using System;
using System.Collections.Generic;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot
{
    /// <summary>One placed attraction as a guest sees it: the numbers the ride score wants, and the
    /// tile to walk to.</summary>
    sealed class GuestTarget
    {
        public int Id;
        public int TypeIndex;
        /// <summary>The ride score's "slot 53": the record's base intensity, which is the same number
        /// behaviour.md §2.4 matches against the guest's taste when it gets off.</summary>
        public int Intensity;
        /// <summary>The ride score's "slot 54". ⚠ ZERO for every real ride (rides.md §0 item 1, READ),
        /// which gates both desire terms off; only a FEATURE whose record says guests may use it ever
        /// gets them. That is the whole reason this can be wired without knowing slots 55 and 56.</summary>
        public int Usable;
        public bool Open;
        /// <summary>Where a guest heads for: the attraction's entrance tile, or its centre when it has
        /// none (shops, features and sideshows have no entrance tile).</summary>
        public int DoorX, DoorZ;
        /// <summary>The footprint's centre tile, which is what the score measures distance to.</summary>
        public int CentreX, CentreZ;

        /// <summary>Where a rider is PUT DOWN when it gets off, or (-1,-1) when the record has no exit.
        /// ⭐ A SEPARATE TILE FROM THE DOOR AND A SEPARATE CONNECTIVITY QUESTION: a ride whose entrance
        /// is reachable can still strand everyone it unloads, because they leave through the other side.</summary>
        public int ExitX = -1, ExitZ = -1;

        /// <summary>The placed stall itself, for the half of a visit that is per-INSTANCE: its price,
        /// its sliders, and where the money it takes goes. Null for anything that is not a shop or a
        /// sideshow — and the purchase code is only reached for types 4 and 5.</summary>
        public IShopSite Site;

        /// <summary>Record +0x2E bit 1 on a feature: staff may rest here (READ, 0x8002433C via the
        /// wrapper 0x80024110). Staff state 49 searches the object list for the NEAREST one of these
        /// whose status byte is also non-zero (0x800660DC).</summary>
        public bool StaffMayRest;

        /// <summary>Status byte A+0x6E is non-zero, i.e. past "just placed". The rest search tests it
        /// alongside the flag above; it is NOT the same as <see cref="Open"/>, which is the three
        /// statuses a guest may queue at.</summary>
        public bool Built;
    }

    /// <summary>The park as TPW.Sim.VisitorDecision reads it (behaviour.md §2.2).
    ///
    /// ⭐ THIS IS WHERE THE TABLES OUT OF THE EXECUTABLE FINALLY GET USED. The need surface and the
    /// desire curve have been sitting in VisitorTables since they were extracted, scoring nothing. With
    /// this the guests stop walking to arbitrary tiles and start going to things they actually want.
    ///
    /// ⚠ THE CANDIDATE LIST IS PER GUEST, because the score includes the distance from the guest. It is
    /// rebuilt on <see cref="SetGuest"/> and read straight after; holding one of these across guests
    /// would score everyone from wherever the last one stood.</summary>
    sealed class GuestBrain : IDecisionWorld
    {
        readonly Func<IReadOnlyList<GuestTarget>> _targets;
        readonly List<AttractionCandidate> _candidates = new();
        readonly Func<Guest, int, int, bool> _pathTo;
        Guest _guest;

        public GuestBrain(ParkMap map, Func<IReadOnlyList<GuestTarget>> targets,
                          Func<Guest, int, int, bool> pathToTile, Func<long> now)
        {
            MapWidth = map.Width;
            MapHeight = map.Height;
            _targets = targets;
            _pathTo = pathToTile;
            _now = now;
        }

        readonly Func<long> _now;
        public long NowTick => _now();
        public int MapWidth { get; }
        public int MapHeight { get; }

        /// <summary>The attraction the last decision settled on, for the caller to walk to.</summary>
        public GuestTarget Chosen { get; private set; }

        /// <summary>Point the world at a guest and rebuild the candidate list around it.</summary>
        public void SetGuest(Guest g)
        {
            _guest = g;
            Chosen = null;
            _candidates.Clear();

            int gx = g.X >> 8, gz = g.Z >> 8;
            foreach (var t in _targets())
            {
                int d = Math.Abs(t.CentreX - gx) + Math.Abs(t.CentreZ - gz);
                bool onMap = t.CentreX >= 0 && t.CentreX < MapWidth - 1
                          && t.CentreZ >= 0 && t.CentreZ < MapHeight - 1;
                // Slots 55 and 56 are passed as zero: NOT a claim that they are zero, but the value
                // that makes the desire terms contribute nothing, which is what slot 54 already forces
                // for every ride. When someone reads the ride vtable they replace these two and
                // nothing else in this file has to change.
                _candidates.Add(new AttractionCandidate(
                    id: t.Id, typeIndex: t.TypeIndex, slot53: t.Intensity, slot54: t.Usable,
                    slot55: 0, slot56: 0, openToGuests: t.Open, distanceTiles: d,
                    centreTileValid: onMap));
            }
        }

        public IReadOnlyList<AttractionCandidate> OpenAttractions => _candidates;

        /// <summary>The decision's last step: ask the pathfinder for a route to what it chose.
        ///
        /// ⚠ FALSE HERE IS "THE REQUEST WAS REFUSED", not "unreachable". The guest keeps its target and
        /// tries again, which is what the game's own callers do.</summary>
        public bool TryPathToTarget(Visitor guest)
        {
            foreach (var t in _targets())
                if (t.Id == guest.ChosenId) { Chosen = t; return _pathTo(_guest, t.DoorX, t.DoorZ); }
            return false;
        }
    }
}
