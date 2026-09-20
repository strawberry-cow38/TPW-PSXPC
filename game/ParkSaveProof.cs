using System;
using System.Linq;
using System.Text.Json;
using Godot;
using TPW.Data;
using TPW.Sim;

namespace TPWGodot;

/// <summary>Executable integration assertions, run inside Godot against real disc assets.
/// All numbers here are deliberately distinctive TEST INPUTS, not inferred game constants.
/// tools/prove_savehost.py compares live snapshots from two separate Godot processes.</summary>
internal static class ParkSaveProof
{
    internal static void Require(bool ok, string rejects)
    {
        if (!ok) throw new InvalidOperationException("SAVE PROOF rejects: " + rejects);
    }

    // REJECTS an empty-park round trip or a read process seeded from the writer's live state.
    internal static void AssertFresh(ParkView park, ParkSaveHost host)
    {
        Require(park.SaveGuests.Count == 0 && park.SaveGuests.StaffCount == 0
            && host.Capture().Attractions.Sum(g => g.Count) == 0, "load did not start in a fresh park");
        GD.Print("[save-proof] fresh " + park.SaveProofSnapshot());
    }

    // REJECTS capture before CLI construction/upgrades/hires, default-only fields and fake admissions.
    internal static void Seed(ParkView park, ParkSaveHost host)
    {
        park.SeedSaveProof(host);
        // REJECTS a symmetrically wrong writer/reader staff block order: round-trip equality alone
        // cannot detect both halves making the same mistake (guard, researcher, mechanic, cleaner, entertainer).
        Require(host.Capture().Staff.Select(g => g.Single().HireDay).SequenceEqual(new[] { 806, 807, 803, 805, 804 }),
            "staff wire blocks must follow save.md independently of StaffKind enum order");
        GD.Print("[save-proof] before " + park.SaveProofSnapshot());
    }

    // REJECTS success with missing live objects, stale bank/calendar references, reset levels/fees.
    internal static void Check(ParkView park, ParkSaveHost host)
    {
        park.CheckSaveProof();
        GD.Print("[save-proof] after " + park.SaveProofSnapshot());
        // A second load into an occupied park rejects old staff/guest nodes and duplicate pools.
        var before = park.SaveProofSnapshot();
        var oldGuests = park.SaveGuests;
        var oldBodies = oldGuests.SaveStaff.Select(s => s.Inst).Concat(oldGuests.SaveVisitors.Select(g => g.Inst)).ToArray();
        byte[] saved = ParkSaving.Save(host);
        ParkSaving.Load(saved, host.Layout, host);
        Require(!ReferenceEquals(oldGuests, park.SaveGuests) && oldGuests.Count == 0 && oldGuests.StaffCount == 0
            && oldBodies.All(b => b.IsQueuedForDeletion()), "reload retained old people/nodes");
        Require(before == park.SaveProofSnapshot(), "reload into occupied park accumulated/lost state");
        Require(park.SaveGuests.SaveVisitors.All(g => g.V.State == VisitorState.WalkIn && !g.V.HasTarget),
            "visitors retained old ride/path state or are charged entry again");
        // Control: a different map must fail before BeginPark changes the live host.
        var wrong = (byte[])saved.Clone(); wrong[2] ^= 1;
        bool refused = false;
        try { ParkSaving.Load(wrong, host.Layout, host); }
        catch (FormatException) { refused = true; }
        Require(refused && before == park.SaveProofSnapshot(), "wrong-map control changed the park");
        // REJECTS leaving the already-open gate open when a closed save is loaded over it.
        park.ParkOpen = false;
        byte[] closed = ParkSaving.Save(host);
        park.ParkOpen = true;
        ParkSaving.Load(closed, host.Layout, host);
        Require(!park.ParkOpen && !park.SaveGuests.ParkIsOpen, "closed park flag retained the old open gate");
        ParkSaving.Load(saved, host.Layout, host);
        GD.Print("[save-proof] PASS: fresh load, occupied reload, wrong-map control");
    }
}

public partial class ParkView
{
    internal void SeedSaveProof(ParkSaveHost host)
    {
        ParkSaveProof.Require(_attractionsPlaced.Count == 4 && _guests.StaffCount == 5,
            "CLI fixture must place four attractions and hire all five staff classes before save");
        ParkSaveProof.Require(_attractionsPlaced.Single(a => a.Rec.Entry == 217).Level == 1,
            "--park-upgrade did not run after placement and before save");
        ParkSaveProof.Require(UpgradeNow(220) == 1 && UpgradeNow(220) == 2, "paid upgrades are not live");
        var queued = _attractionsPlaced.Single(a => a.Rec.Entry == 220);
        var start = queued.Queue.End;
        var direction = queued.Rec.EntranceTurn(queued.Rot) switch
        { 0 => (X: 0, Z: -1), 1 => (X: -1, Z: 0), 2 => (X: 0, Z: 1), _ => (X: 1, Z: 0) };
        ParkSaveProof.Require(queued.Queue.Lay(_map, start.X + direction.X * 3, start.Z + direction.Z * 3) != QueueRun.Step.Refused,
            $"queue fixture from {start} toward {direction}");
        _guests.MapChanged();
        host.OpenPark(); host.SetEntryFee(Money.FromPounds(7));
        // Let real guests through the real turnstile. No increment of the admission counter here.
        for (int i = 0; i < 9; i++) ParkSaveProof.Require(_guests.SpawnAtGate() != null, "guest spawning");
        for (int i = 0; i < 6000 && _guests.SaveEntrance.Counter_McAi1C < 2; i++)
        {
            _guests.RunPathfinder(); _guests.Tick();
        }
        ParkSaveProof.Require(_guests.SaveEntrance.Counter_McAi1C == 2 && _guests.Count == 9,
            "guests must actually pay and enter: " + _guests.GateReport());
        GD.Print("[save-proof] admissions " + _guests.GateReport());
        foreach (var guest in _guests.SaveVisitors)
        {
            guest.V.Rubbish = 23; guest.V.Happiness = 64; guest.V.Money = Money.FromRaw(876);
            guest.V.WalkSpeed = 19; guest.Unknown63 = 151;
        }
        // Stop on a known calendar day without spending hundreds of wall-clock seconds.
        SaveFinances.Calendar.RestoreTo(2, 3, 16, 836, 27);
        RestoreParkTick(836L * ParkClock.TicksPerDay);
        for (int i = 0; i < _guests.SaveStaff.Count; i++)
        {
            var st = _guests.SaveStaff[i];
            st.X = ParkGuests.Centre(20 + i) + 13; st.Z = ParkGuests.Centre(21) - 9;
            st.HiredDay = 803 + i; st.S.Skill = i;
            st.S.SetState(i == 4 ? StaffState.Striking : StaffState.Idle);
        }
        foreach (var a in _attractionsPlaced)
        {
            a.BuiltOnDay = 811 + a.Rot;
            a.Status = a.IsRide ? AttractionStatus.ClosedByPlayer : AttractionStatus.Running;
            a.Served = 17 + a.Rot;
            var runtime = _guests.Rides.RuntimeFor(a.Rec.Entry);
            runtime.Served = a.Served;
            if (a.IsRide)
            {
                a.SpeedSlider = 67 + a.Rot; a.Capacity = 3 + a.Rot; a.CyclesPerLoad = 4 + a.Rot;
                a.ReliabilityFixed = (73 + a.Rot) << 12; a.Lifetime = 41 + a.Rot;
            }
            else if (a.Rec.Type == 2) runtime.Stock = FeatureStock.FromSave(63, 819);
            else if (a.Rec.Type == 4)
            {
                a.SalePrice = 19; a.Takings = Money.FromRaw(1234); a.Profit = Money.FromRaw(-321);
                a.Visits = 11; a.Satisfaction = 77 * a.Visits; a.QualitySlider = 61; a.SecondSlider = 43;
            }
        }
        // Also exercise a taken-but-repaid loan slot, whose availability must survive saving.
        SaveFinances.Loans[0].Grant(Money.FromPounds(2468), Money.FromPounds(73), 19);
        SaveFinances.Loans[2].Grant(Money.Zero, Money.FromPounds(53), -2);
        SaveFinances.Bank.Receive(Money.FromRaw(314159) - SaveFinances.Bank.Balance);
        CheckSaveProof();
    }

    internal void CheckSaveProof()
    {
        ParkSaveProof.Require(_attractionsPlaced.Count == 4 && _placed.Count == 4, "four placed models");
        ParkSaveProof.Require(_attractionsPlaced.Single(a => a.Rec.Entry == 220).Level == 2
            && _attractionsPlaced.Single(a => a.Rec.Entry == 217).Level == 1, "distinct upgraded levels");
        ParkSaveProof.Require(SaveFinances.Bank.Balance.Raw == 314159, "raw bank tenths/reference");
        ParkSaveProof.Require(SaveFinances.Calendar.TotalDays == 836 && SaveFinances.Calendar.Day == 16,
            "calendar day/reference");
        ParkSaveProof.Require(ParkOpen && _guests.SaveEntrance.EntryFee == Money.FromPounds(7), "open flag/entry fee");
        ParkSaveProof.Require(_guests.Count == 9 && _guests.StaffCount == 5
            && _guests.SaveEntrance.Counter_McAi1C == 2, "guest/staff/admission counts");
        ParkSaveProof.Require(_guests.SaveStaff.Select(s => s.S.Kind).Distinct().Count() == 5, "staff block order");
        ParkSaveProof.Require(_guests.SaveVisitors.All(g => g.V.Rubbish == 23 && g.V.Happiness == 64
            && g.V.Money.Raw == 876 && g.V.WalkSpeed == 19) && _guests.SaveVisitors.Any(g => g.Unknown63 != 0),
            "visitor ranges/unknown byte were not connected to live visitors");
        ParkSaveProof.Require(_attractionsPlaced.All(a => ReferenceEquals(a.Bank, SaveFinances.Bank)), "stale attraction bank");
    }

    internal string SaveProofSnapshot()
    {
        _guests.Rides.SetRides(GuestTargets());
        var cal = SaveFinances.Calendar;
        // Inspect the live fields directly, independently of Capture's mappings. JSON is diagnostic
        // output only; the file being loaded is exclusively ParkSaving's binary stream.
        return JsonSerializer.Serialize(new
        {
            attractions = _attractionsPlaced.OrderBy(a => a.Rec.Entry).Select(a => new
            {
                entry = a.Rec.Entry, type = a.Rec.Type, x = a.Ox, z = a.Oz, rotation = a.Rot,
                level = a.Level, status = (int)a.Status,
                built = a.Rec.Type == 2 ? 0 : a.BuiltOnDay,
                speed = a.IsRide ? a.SpeedSlider : 0, capacity = a.IsRide ? a.Capacity : 0,
                duration = a.IsRide ? a.CyclesPerLoad : 0, reliability = a.IsRide ? a.ReliabilityFixed : 0,
                lifetime = a.IsRide ? a.Lifetime : 0,
                served = _guests.Rides.RuntimeFor(a.Rec.Entry).Served,
                queue = a.Queue?.Points.Select(p => new[] { p.X, p.Z }).ToArray() ?? Array.Empty<int[]>(),
                price = a.Rec.Type == 4 ? a.SalePrice : 0, takings = a.Takings.Raw, profit = a.Profit.Raw,
                visits = a.Visits, satisfaction = a.Satisfaction, quality = a.QualitySlider, second = a.SecondSlider,
                stock = a.Rec.Type == 2 ? _guests.Rides.RuntimeFor(a.Rec.Entry).Stock.Level : 0,
                serviced = a.Rec.Type == 2 ? _guests.Rides.RuntimeFor(a.Rec.Entry).Stock.LastServicedDay : 0,
            }).ToArray(),
            guests = _guests.Count,
            staff = _guests.SaveStaff.OrderBy(s => s.S.Kind).Select(s => new
            { kind = (int)s.S.Kind, x = s.X, z = s.Z, hired = s.HiredDay, skill = s.S.Skill, striking = s.S.State == StaffState.Striking }).ToArray(),
            bank = SaveFinances.Bank.Balance.Raw, totalDays = cal.TotalDays, day = cal.Day, month = cal.Month,
            year = cal.Year, totalMonths = cal.TotalMonths, open = ParkOpen,
            fee = _guests.SaveEntrance.EntryFee.Raw, admissions = _guests.SaveEntrance.Counter_McAi1C,
            loans = Enumerable.Range(0, LoanBook.Slots).Select(i => new
            { taken = SaveFinances.Loans[i].Taken, remaining = SaveFinances.Loans[i].Remaining.Raw,
                payment = SaveFinances.Loans[i].MonthlyPayment.Raw, months = SaveFinances.Loans[i].MonthsLeft }).ToArray(),
            paths = _map.Tiles.Select((t, i) => (t, i)).Where(p => p.t.Type is TileType.Path or TileType.PathQueueOverlap)
                .Select(p => p.i).ToArray(),
            queues = _map.Tiles.Select((t, i) => (t, i)).Where(p => p.t.Type == TileType.QueuePath).Select(p => p.i).ToArray(),
        });
    }
}
