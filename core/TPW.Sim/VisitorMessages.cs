using System;

namespace TPW.Sim
{
    /// <summary>READ: the guest slot-40 ids with effects, 0x8008F880 / 0x800E3B74 (§2.10).
    /// These are message ids, not visitor state numbers. Unlisted ids are ignored.</summary>
    public enum VisitorMessage
    {
        PathReady = 1,
        PathFailed = 2,
        /// <summary>READ: despawn. The guard sender's identification was GUESS in §2.10;
        /// behaviour.md §0 item 7 subsequently traces the guard's message-4 calls.</summary>
        ThrownOut = 4,
        Shuffle = 6,
        RemovedFromQueue = 7,
        Admit = 9,
        Ejected = 10,
    }

    /// <summary>The two existing park views needed by the single visitor message handler.</summary>
    public interface IVisitorMessageWorld : IEntranceWorld, IQueueWorld { }

    /// <summary>One owner for the complete message table. READ: the binary dispatches once by id;
    /// it does not install a different handler when a guest changes state. The state gates belong
    /// to rows 1/2 (11 only) and 6 (18/44 only). Rows 4/7/9/10 are unconditional.
    /// Existing entrance and queue entry points remain usable as narrow views of this same table;
    /// arrival is a separate slot-35 purpose table, not another message handler.</summary>
    public static class VisitorMessages
    {
        public static bool OnMessage(Visitor guest, IVisitorMessageWorld world, VisitorMessage id,
            IRandomSource rng, int param1 = 0, int param2 = 0)
        {
            if (guest == null) throw new ArgumentNullException(nameof(guest));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            switch (id)
            {
                case VisitorMessage.PathFailed:
                    // The one row missing from the entrance's partial purpose switch.
                    if (guest.State != VisitorState.WalkToBin) return false;
                    if (guest.Purpose == Purpose.QueueWalk)
                    {
                        if (guest.InQueue) VisitorQueue.LeaveQueue(guest, world);
                        else
                        {
                            guest.HasTarget = false;
                            guest.SetState(VisitorState.Idle);
                        }
                        return true;
                    }
                    return VisitorEntrance.OnMessage(guest, world, EntranceMessage.PathFailed, rng);
                case VisitorMessage.PathReady:
                    return VisitorEntrance.OnMessage(guest, world, EntranceMessage.PathReady, rng);
                case VisitorMessage.ThrownOut:
                    return VisitorEntrance.OnMessage(guest, world, EntranceMessage.ThrownOut, rng);
                case VisitorMessage.Admit:
                    return VisitorEntrance.OnMessage(guest, world, EntranceMessage.Admit, rng);
                case VisitorMessage.Shuffle:
                    return VisitorQueue.OnMessage(guest, world, QueueMessage.Shuffle, param1, param2);
                case VisitorMessage.RemovedFromQueue:
                    return VisitorQueue.OnMessage(guest, world, QueueMessage.RemovedFromQueue);
                case VisitorMessage.Ejected:
                    return VisitorQueue.OnMessage(guest, world, QueueMessage.Ejected);
                default:
                    return false;
            }
        }
    }
}
