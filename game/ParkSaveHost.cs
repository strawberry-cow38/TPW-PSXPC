using System;
using System.Buffers.Binary;
using System.Linq;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot;

/// <summary>The unpacked save stream over the live park. Wire offsets are READ in findings/save.md
/// §3; zero-filled UNKNOWN/unmodeled storage is a port policy, not a claim about PSX RAM.
/// No card header, compression, or alternate JSON snapshot is introduced.</summary>
internal sealed class ParkSaveHost : IParkSaveHost
{
    readonly ParkView park;
    readonly Action resetSession;
    readonly Action<long> setClock;
    public ParkSaveLayout Layout { get; }
    ParkGuests Guests => park.SaveGuests;
    ParkFinances Finances => park.SaveFinances;

    public ParkSaveHost(ParkView park, ParkSaveLayout layout, Action resetSession, Action<long> setClock)
    {
        this.park = park ?? throw new ArgumentNullException(nameof(park));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        this.resetSession = resetSession ?? throw new ArgumentNullException(nameof(resetSession));
        this.setClock = setClock ?? throw new ArgumentNullException(nameof(setClock));
    }

    internal static NotSupportedException NotWired(string missing) =>
        new($"ParkSaveHost: {missing} is not wired; refusing to silently discard saved state.");

    // Unmodeled sections are zero on capture. Accept precisely that empty representation on load;
    // a nonempty external save must fail by name, never appear to have been restored.
    internal static void RequireZero(ReadOnlySpan<byte> bytes, string missing)
    {
        foreach (byte b in bytes) if (b != 0) throw NotWired(missing);
    }

    public ParkSave Capture()
    {
        var cal = Finances.Calendar;
        var result = new ParkSave(Layout)
        {
            Open = park.ParkOpen ? (byte)1 : (byte)0,
            EntryFeePounds = checked((ushort)Guests.SaveEntrance.EntryFee.Pounds),
            Paths = ParkSaveCodec.CapturePaths(Layout, (x, z) => park.SaveMap[x, z].Raw0),
            VisitorCount = checked((byte)Guests.Count),
            Visitors = VisitorSaveRanges.Capture(Guests.SaveVisitors.Select(g => (g.V, g.Unknown63))),
            Bank = CaptureBank(),
            Calendar = new CalendarSave
            {
                TotalDays = checked((uint)cal.TotalDays), Year = checked((ushort)cal.Year),
                TotalMonths = unchecked((ushort)cal.TotalMonths), Month = checked((byte)cal.Month),
                Day = checked((byte)cal.Day), Admissions = unchecked((uint)Guests.SaveEntrance.Counter_McAi1C),
                MonthsInDebt = checked((byte)Finances.Debt.MonthsInDebt),
            },
        };
        if (Guests.Research is { } research)
        {
            result.ResearchTopics = research.SaveTopics();
            result.Catalogue = ResearchSave.CaptureCatalogue(Layout, research);
        }
        park.CaptureAttractions(result);
        foreach (var st in Guests.SaveStaff)
        {
            int block = Array.IndexOf(StaffOrder, st.S.Kind);
            if (block < 0) throw NotWired($"staff class {st.S.Kind}");
            result.Staff[block].Add(new StaffSave
            {
                X = checked((short)st.X), Y = checked((short)st.Z), HireDay = st.HiredDay,
                SkillAndStrike = (byte)((st.S.Skill & 7) | (st.S.State == StaffState.Striking ? 0x80 : 0)),
                // Recruit variants/patrol UI do not exist in the live port. Refuse nonzero on load.
            });
        }
        // GAP: no live research catalogue/topics, messages, influence map or litter pool. The
        // ParkSave constructor zeroes catalogue/topics; messages/litter stay empty. Influence has
        // no independent field in this stream. Bank/calendar histories likewise have no live owner.
        return result;
    }

    BankSave CaptureBank()
    {
        var saved = new BankSave { BalanceRaw = checked((int)Finances.Bank.Balance.Raw) };
        for (int i = 0; i < LoanBook.Slots; i++)
        {
            var loan = Finances.Loans[i];
            int at = 0x280 + i * 0x1C; // READ: 0x80086470, save.md §3.5.
            Put32(saved.Bytes, at + 8, loan.MonthsLeft);
            Put32(saved.Bytes, at + 12, checked((int)loan.Remaining.Pounds));
            Put32(saved.Bytes, at + 16, checked((int)loan.MonthlyPayment.Pounds));
            saved.Bytes[at + 24] = loan.Taken ? (byte)0 : (byte)1;
            // Original principal/term/total repayable are not retained by the live Loan model.
        }
        return saved;
    }

    public void BeginPark(ParkSaveLayout layout)
    {
        if (layout.World != Layout.World || layout.Park != Layout.Park || layout.Width != Layout.Width
            || layout.Height != Layout.Height || layout.Restricted != Layout.Restricted
            || !layout.CatalogueCounts.SequenceEqual(Layout.CatalogueCounts))
            throw new ArgumentException("Save layout does not match this park's assets.");
        resetSession(); // Fresh bank, loans, debt, calendar and main clock, wired back into Main/HUD.
        park.ReloadForSave(); // Fresh original map, people, pathfinder, ride claims, gate and bus.
        park.ParkOpen = layout.Restricted;
        Guests.ParkIsOpen = park.ParkOpen;
    }
    public void OpenPark() { park.ParkOpen = true; Guests.ParkIsOpen = true; }
    public void SetEntryFee(Money fee) => Guests.SaveEntrance.EntryFee = fee;
    public void PlacePath(int x, int y) => park.RestorePath(x, y);
    public void RestoreAttraction(AttractionSave saved) => park.RestoreSavedAttraction(saved);
    public Visitor CreateVisitor() => Guests.CreateSavedVisitor();
    public IRandomSource Random => Guests.Dice;
    public void SetVisitorUnknown63(Visitor visitor, byte value) => Guests.SetSavedVisitorByte(visitor, value);

    // READ: save.md §3.4, not StaffKind's enum order.
    internal static readonly StaffKind[] StaffOrder =
        { StaffKind.Guard, StaffKind.Researcher, StaffKind.Mechanic, StaffKind.Cleaner, StaffKind.Entertainer };
    public void RestoreStaff(int block, StaffSave saved)
    {
        if ((uint)block >= StaffOrder.Length) throw new ArgumentOutOfRangeException(nameof(block));
        if (saved.Variant != 0) throw NotWired("staff recruit variants");
        RequireZero(saved.Bytes.AsSpan(12, 4), "staff patrol rectangles");
        var st = Guests.Hire(StaffOrder[block], saved.X >> 8, saved.Y >> 8);
        st.X = saved.X; st.Z = saved.Y; st.HiredDay = saved.HireDay;
        st.S.Skill = saved.SkillAndStrike & 7;
        st.S.SetState(saved.RestoredState); // Existing constructor retains morale 100/tiredness 0.
    }
    public void RestoreLitter(byte ordinary, byte vomit) =>
        RequireZero(new[] { ordinary, vomit }, "live litter pool");

    public void RestoreBank(BankSave saved)
    {
        RequireZero(saved.Bytes.AsSpan(0, 0x280), "bank history expansion");
        RequireZero(saved.Bytes.AsSpan(0x2F4), "bank historical totals");
        for (int i = 0; i < LoanBook.Slots; i++)
        {
            int at = 0x280 + i * 0x1C;
            RequireZero(saved.Bytes.AsSpan(at, 8), "loan principal/term metadata");
            RequireZero(saved.Bytes.AsSpan(at + 20, 4), "loan total repayable metadata");
            if (saved.Bytes[at + 24] == 0)
                Finances.Loans[i].Grant(Money.FromPounds(Get32(saved.Bytes, at + 12)),
                    Money.FromPounds(Get32(saved.Bytes, at + 16)), Get32(saved.Bytes, at + 8));
        }
        Finances.Bank.Receive(Money.FromRaw(saved.BalanceRaw) - Finances.Bank.Balance);
    }
    public void RestoreCalendar(CalendarSave saved)
    {
        RequireZero(saved.Bytes.AsSpan(0, 20), "staff strike deadlines");
        RequireZero(saved.Bytes.AsSpan(32, 7), "annual rating/class strike state");
        RequireZero(saved.Bytes.AsSpan(43, 175), "calendar history expansion");
        if (saved.Month >= Calendar.MonthLengths.Length || saved.Day >= Calendar.MonthLengths[saved.Month]
            || saved.TotalDays > int.MaxValue || saved.MonthsInDebt > DebtWatch.MonthsToBankruptcy)
            throw new FormatException("Invalid saved calendar/debt state.");
        Finances.Calendar.RestoreTo(saved.Year, saved.Month, saved.Day, (int)saved.TotalDays, saved.TotalMonths);
        Guests.SaveEntrance.Counter_McAi1C = unchecked((int)saved.Admissions);
        // DebtWatch has a public transition, no raw setter; run it only on the fresh debt object.
        for (int i = 0; i < saved.MonthsInDebt; i++) Finances.Debt.MonthEnd(Money.FromRaw(-1), Finances.Loans.AnyOutstanding);
        long tick = (long)saved.TotalDays * ParkClock.TicksPerDay;
        setClock(tick);
        park.RestoreParkTick(tick);
    }
    /// <summary>⭐ THE RESEARCH TREE ROUND-TRIPS NOW. Both of these used to REFUSE any non-zero
    /// section, because nothing in the port owned a ResearchSystem — so a park that had researched
    /// anything could not be saved and reloaded at all. The system exists as of today; these are the
    /// other half of it.
    ///
    /// ⚠ CATALOGUE BEFORE TOPICS, and the sim's own interface comment says so: a topic references a
    /// definition's progress, so restoring the topics first points them at percentages that have not
    /// been put back yet.</summary>
    public void RestoreCatalogue(byte[] percentAndCompleted)
    {
        if (Guests.Research is not { } r) { RequireZero(percentAndCompleted, "live research catalogue"); return; }
        ResearchSave.RestoreCatalogue(Layout, percentAndCompleted, r);
    }

    public void RestoreResearchTopics(byte[] topics)
    {
        if (Guests.Research is not { } r) { RequireZero(topics, "live research topics"); return; }
        r.RestoreTopics(topics);
    }
    public void RestoreMessage(ParkSaveMessage saved) => throw NotWired("park message manager");
    public void FinishPark() => park.FinishSavedPark();

    internal static int Get32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    internal static void Put32(byte[] b, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), value);
    internal static short Get16(byte[] b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at));
    internal static void Put16(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), unchecked((ushort)value));
}

public partial class ParkView
{
    Action _reloadForSave;
    internal ParkMap SaveMap => _map ?? throw ParkSaveHost.NotWired("loaded map");
    internal ParkGuests SaveGuests => _guests ?? throw ParkSaveHost.NotWired("guest manager");
    internal ParkFinances SaveFinances => _finances ?? throw ParkSaveHost.NotWired("park finances");
    internal void ReloadForSave() => (_reloadForSave ?? throw ParkSaveHost.NotWired("original map/assets"))();
    internal ParkSaveLayout SaveLayout(int mapEntry)
    {
        var world = ParkWorlds.ForMap(mapEntry) ?? throw ParkSaveHost.NotWired("world catalogue");
        // Port's current catalogue is the union documented in AttractionCatalog. This is a port
        // file layout, not a claim to have loaded the PSX scenario's research definitions (type 8 too).
        return new ParkSaveLayout(checked((byte)world.Index), checked((byte)Array.IndexOf(world.Maps, mapEntry)),
            SaveMap.Width, SaveMap.Height, false,
            ParkSaveLayout.CatalogueTypes.Select(t => _attractions.Count(a => a.Rec.Type == t)).ToArray());
    }
    PlacedAttraction PlacedForSave(AttractionDefinition rec, int x, int z, int rot) =>
        _attractionsPlaced.Last(a => a.Rec == rec && a.Ox == x && a.Oz == z && a.Rot == rot);

    internal void RestorePath(int x, int z)
    {
        if (_paths == null) throw ParkSaveHost.NotWired("path tool/assets");
        if (x < 0 || z < 0 || x >= _map.Width || z >= _map.Height) throw new FormatException("Path outside map.");
        _paths.Lay(_map, PathTool.Run(x, z, x, z)); // Both calls from ParkSaving.Load are intentional.
        if (_map[x, z].Type is not (TileType.Path or TileType.PathQueueOverlap))
            throw new FormatException($"Cannot restore path at ({x},{z}).");
    }

    internal void CaptureAttractions(ParkSave save)
    {
        if (_attractionsPlaced.Count(a => a.Track is { Pylons.Count: > 0 }) > 1)
            throw ParkSaveHost.NotWired("multiple track geometry renderer");
        _guests.Rides.SetRides(GuestTargets());
        foreach (var a in _attractionsPlaced)
        {
            var defs = _attractions.Where(d => d.Rec.Type == a.Rec.Type).ToArray();
            var s = new AttractionSave(a.Rec.Type)
            {
                SavedType = checked((byte)a.Rec.Type), Definition = checked((byte)Array.FindIndex(defs, d => d.Rec == a.Rec)),
                X = checked((byte)a.Ox), Y = checked((byte)a.Oz), Rotation = checked((byte)a.Rot),
                Status = (byte)a.Status, GuestsServed = a.Served,
            };
            var runtime = _guests.Rides.RuntimeFor(a.Rec.Entry) ?? throw ParkSaveHost.NotWired("ride runtime");
            if (a.IsRide)
            {
                s.GuestsServed = runtime.Served;
                s.PlacementDay = unchecked((ushort)a.BuiltOnDay); s.Sliders = TPW.Sim.RidePanel.SaveSliders(a);
                s.ReliabilityFixed = a.ReliabilityFixed; s.Level = checked((byte)a.Level);
                s.Bytes[0x8E] = unchecked((byte)a.Lifetime); // READ 0x8009CE40, narrowed lifetime.
                var points = a.Queue?.Points;
                s.Bytes[0x91] = checked((byte)(points?.Count ?? 0));
                if (points != null) for (int i = 0; i < points.Count; i++)
                {
                    ParkSaveHost.Put16(s.Bytes, 8 + i * 4, points[i].X);
                    ParkSaveHost.Put16(s.Bytes, 10 + i * 4, points[i].Z);
                }
                if (a.Track is { Pylons.Count: > 0 })
                {
                    // SOURCE DISAGREEMENT: live type 1 is a coaster, save.md gives it only 0x98
                    // bytes. Keep the core format; never stuff coaster geometry in UNKNOWN bytes.
                    if (a.Rec.Type != 6) throw ParkSaveHost.NotWired("coaster geometry: save.md/live class disagreement");
                    if (a.Track.Pylons.Count > 20) throw ParkSaveHost.NotWired("track routes above save.md's 20-point reservation");
                    s.Bytes[0x95] = checked((byte)a.Track.Pylons.Count);
                    for (int i = 0; i < a.Track.Pylons.Count; i++)
                    {
                        ParkSaveHost.Put16(s.Bytes, 0x96 + i * 4, a.Track.Pylons[i].X);
                        ParkSaveHost.Put16(s.Bytes, 0x98 + i * 4, a.Track.Pylons[i].Z);
                    }
                }
            }
            else if (a.Rec.Type == 2)
            {
                ParkSaveHost.Put32(s.Bytes, 8, runtime.Stock.LastServicedDay);
                s.Bytes[14] = unchecked((byte)runtime.Stock.Level);
            }
            else if (a.Rec.Type == 4)
            {
                ParkSaveHost.Put32(s.Bytes, 8, a.Visits);
                ParkSaveHost.Put16(s.Bytes, 12, a.SalePrice);
                ParkSaveHost.Put16(s.Bytes, 14, checked((int)a.Takings.Raw));
                ParkSaveHost.Put16(s.Bytes, 16, checked((int)a.Profit.Raw));
                ParkSaveHost.Put16(s.Bytes, 18, a.BuiltOnDay);
                s.Bytes[22] = unchecked((byte)a.SecondSlider);
                s.Bytes[23] = unchecked((byte)(a.Visits == 0 ? 0 : a.Satisfaction / a.Visits));
                s.Bytes[24] = unchecked((byte)a.QualitySlider);
            }
            else throw ParkSaveHost.NotWired("sideshow counter mapping (save.md §3.2 UNKNOWN semantics)");
            save.Attractions[AttractionSave.Types.ToList().IndexOf(s.Type)].Add(s);
        }
    }

    internal void RestoreSavedAttraction(AttractionSave s)
    {
        var defs = _attractions.Where(a => a.Rec.Type == s.Type).ToArray();
        if (s.Definition >= defs.Length || s.SavedType != s.Type || s.Rotation > 3)
            throw new FormatException("Invalid saved attraction definition/type/rotation.");
        if (s.Type == 5) throw ParkSaveHost.NotWired("sideshow counter mapping");
        var rec = defs[s.Definition].Rec;
        // The bitmap includes path/queue overlaps. Placement validates against the rebuilt map.
        // Charges are bypassed; the saved bank is restored later, as required by ParkSaving.
        var bank = _bank;
        try
        {
            _bank = null;
            if (!PlaceAt(rec.Entry, s.X, s.Y, s.Rotation)) throw new FormatException($"Cannot restore attraction {rec.Entry} at ({s.X},{s.Y}).");
        }
        finally { _bank = bank; }
        var a = _attractionsPlaced[^1]; a.Bank = bank; a.Served = s.GuestsServed;
        a.Status = (AttractionStatus)s.RestoredStatus;
        _guests.Rides.SetRides(GuestTargets());
        var runtime = _guests.Rides.RuntimeFor(rec.Entry) ?? throw ParkSaveHost.NotWired("restored ride runtime");
        runtime.Served = s.GuestsServed;
        if (a.IsRide)
        {
            a.Level = s.Level; a.BuiltOnDay = s.PlacementDay;
            TPW.Sim.RidePanel.RestoreSliders(a, s.Sliders);
            a.ReliabilityFixed = s.ReliabilityFixed; a.Lifetime = s.Bytes[0x8E];
            int count = s.Bytes[0x91];
            if (count > QueueRun.MaxPoints) throw new FormatException("Too many queue points.");
            if (count > 0)
            {
                a.Queue = _paths?.StartQueue(rec, a.Ox, a.Oz, a.Rot) ?? throw ParkSaveHost.NotWired("queue geometry");
                if (a.Queue.End != (ParkSaveHost.Get16(s.Bytes, 8), ParkSaveHost.Get16(s.Bytes, 10)))
                    throw new FormatException("Queue does not start at its attraction entrance.");
                for (int i = 1; i < count; i++)
                    if (a.Queue.Lay(_map, ParkSaveHost.Get16(s.Bytes, 8 + i * 4), ParkSaveHost.Get16(s.Bytes, 10 + i * 4)) == QueueRun.Step.Refused)
                        throw new FormatException("Cannot rebuild saved queue.");
            }
            if (s.Type == 6)
            {
                ParkSaveHost.RequireZero(s.Bytes.AsSpan(0xE6, 7), "track secondary records");
                int n = s.Bytes[0x95];
                if (n > 20) throw new FormatException("Too many track points.");
                if (n > 0)
                {
                    a.Track = new TrackRun(rec, a.Ox, a.Oz, a.Rot, _worldIndex);
                    for (int i = 0; i < n; i++)
                        if (a.Track.Lay(_map, ParkSaveHost.Get16(s.Bytes, 0x96 + i * 4), ParkSaveHost.Get16(s.Bytes, 0x98 + i * 4)) == TrackRun.Step.Refused)
                            throw new FormatException("Cannot rebuild saved track.");
                }
            }
            else if (s.Type == 7) ParkSaveHost.RequireZero(s.Bytes.AsSpan(0x95, 0x381), "coaster piece fields/live tour class disagreement");
            else if (s.Type == 3 && s.Bytes[0x95] != 0) throw ParkSaveHost.NotWired("tour route flag/live flat ride class disagreement");
        }
        else if (s.Type == 2) runtime.Stock = FeatureStock.FromSave(unchecked((sbyte)s.Bytes[14]), ParkSaveHost.Get32(s.Bytes, 8));
        else if (s.Type == 4)
        {
            a.Visits = ParkSaveHost.Get32(s.Bytes, 8);
            a.SalePrice = unchecked((ushort)ParkSaveHost.Get16(s.Bytes, 12));
            a.Takings = Money.FromRaw(ParkSaveHost.Get16(s.Bytes, 14));
            a.Profit = Money.FromRaw(ParkSaveHost.Get16(s.Bytes, 16));
            a.BuiltOnDay = unchecked((ushort)ParkSaveHost.Get16(s.Bytes, 18));
            a.SecondSlider = s.Bytes[22]; a.Satisfaction = s.Bytes[23] * a.Visits; a.QualitySlider = s.Bytes[24];
        }
    }

    internal void RestoreParkTick(long tick) { _clockTicks = tick; _guests.RestoreTick(tick); }
    internal void FinishSavedPark()
    {
        if (_paths == null) throw ParkSaveHost.NotWired("path link rebuild");
        _paths.RefreshLinks(_map, 0, 0, _map.Width - 1, _map.Height - 1);
        _guests.MapChanged(); _guests.Rides.SetRides(GuestTargets());
        RebuildGround();
        var tracks = _attractionsPlaced.Where(a => a.Track != null).ToArray();
        // RebuildTrackPieces currently renders a single tool's track. Refuse multiple tracks until
        // that renderer owns per-attraction nodes; otherwise loading would silently hide geometry.
        if (tracks.Length > 1) throw ParkSaveHost.NotWired("multiple track geometry renderer");
        if (tracks.Length == 1) { _track = tracks[0].Track; RebuildTrackPieces(); _track = null; }
        _guests.Redraw(); RefreshPickerPrices(); RefreshInfo();
    }
}

internal sealed partial class ParkGuests
{
    internal System.Collections.Generic.IReadOnlyList<Guest> SaveVisitors => _guests;
    internal System.Collections.Generic.IReadOnlyList<Staffer> SaveStaff => _staff;
    internal ParkEntranceWorld SaveEntrance => _entrance ?? throw ParkSaveHost.NotWired("turnstile/entry fee (--park-nogate)");
    internal void RestoreTick(long tick) => _now = tick;
    internal Visitor CreateSavedVisitor()
    {
        _ = SaveEntrance;
        // READ: 0x80091A3C initializes, then positions via 0x800599A4(1). Do not pay again.
        var guest = Spawn((0, 0)) ?? throw ParkSaveHost.NotWired("visitor spawn on restored map");
        (guest.X, guest.Z) = ParkPoints.Aim(1, _dice);
        return guest.V;
    }
    internal void SetSavedVisitorByte(Visitor visitor, byte value)
    {
        if (!_byVisitor.TryGetValue(visitor, out var guest)) throw new ArgumentException("Visitor does not belong to this park.");
        guest.Unknown63 = value;
        guest.Block = PeopleSheet.GuestBlocks[visitor.VisitorType];
        Place(guest);
    }
}
