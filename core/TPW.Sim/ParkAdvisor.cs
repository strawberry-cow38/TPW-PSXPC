using System;

namespace TPW.Sim
{
    /// <summary>READ 0x800DB960: the advisor's six states, in the order the jump table lists them.</summary>
    public enum AdvisorState
    {
        /// <summary>READ 0x80013208. Boot: he greets the park once, then settles.</summary>
        Starting = 0,
        /// <summary>READ 0x80013298. ⭐ THE ONLY STATE THAT REFRESHES STATISTICS AND RUNS RULES.</summary>
        Idle = 1,
        /// <summary>READ 0x800133C4. Growing from nothing and spinning; the message is shown at the end.</summary>
        Arriving = 2,
        /// <summary>READ 0x8001345C. Talking, for exactly as long as the recording lasts.</summary>
        Speaking = 3,
        /// <summary>READ 0x80013518. Unwinding the spin and shrinking away.</summary>
        Leaving = 4,
        /// <summary>READ 0x800135AC. Away, and not listening: the park runs no statistics at all.</summary>
        Gone = 5,
    }

    /// <summary>READ: the advisor's flag byte at +0. Each bit is tested separately, so a park can have
    /// captions without speech or statistics without either.</summary>
    [Flags]
    public enum AdvisorFlags
    {
        None = 0,
        /// <summary>READ 0x80013F1C. Show the caption.</summary>
        Text = 0x01,
        /// <summary>READ 0x80013F90. Play the recording.</summary>
        Speech = 0x02,
        /// <summary>READ 0x800132A0. Refresh statistics and run rules while idle.</summary>
        Statistics = 0x08,
        /// <summary>READ 0x800135EC. Serve out the full pause after speaking. Without it he is available
        /// again the moment he has gone.</summary>
        Linger = 0x10,
        /// <summary>READ 0x80013C2C. Queue messages instead of delivering each one at once.</summary>
        Queue = 0x20,
        /// <summary>What a park runs with.</summary>
        Park = Text | Speech | Statistics | Linger | Queue,
    }

    /// <summary>A message on its way to the advisor. READ 0x80014118: a rule's message carries no payload;
    /// the two payload fields exist for the messages other systems post.</summary>
    public readonly record struct AdvisorPost(ushort Id, byte Param = 0, uint Value = 0, bool HasPayload = false);

    /// <summary>Everything the state machine needs from the game to deliver a message.</summary>
    public interface IParkAdvisorHost
    {
        /// <summary>READ 0x80018814. Whether his recording is still running. ⭐ THIS IS WHAT ENDS THE
        /// SENTENCE -- <see cref="AdvisorState.Speaking"/> lasts exactly as long as the audio, not a
        /// fixed number of ticks. A host that cannot play speech answers false and he moves straight on,
        /// which is what the original does with speech disabled.</summary>
        bool VoicePlaying { get; }
        /// <summary>READ 0x80018840. Cut him off mid-word.</summary>
        void StopVoice();
        /// <summary>READ 0x8001853C. Play line <paramref name="line"/> of ADVISOR.TPW in the park's language.</summary>
        void Speak(int line);
        /// <summary>READ 0x800141BC → 0x800385AC. Open the caption for string <paramref name="textId"/> of
        /// FOLIO 0x197, with the message's own two payload fields substituted into it.</summary>
        void ShowCaption(int textId, byte param, uint value);
        /// <summary>READ 0x80038C8C. Close it again.</summary>
        void HideCaption();
        /// <summary>Uniform in 0..n-1. READ 0x800C2648.</summary>
        int Random(int n);
    }

    /// <summary>The advisor: who decides when to speak, what to say and how to say it.
    ///
    /// ⭐⭐ THE PORT HAD HIS BRAIN AND NOT HIM. `ParkStatistics` and the 125 decoded rule programs were
    /// finished, tested and called from nowhere, because the thing that calls them is this state machine
    /// and nobody had written it. `findings/statistics.md` §7 says so outright -- "a complete advisor UI
    /// machine ... outside this service" -- and that sentence was the whole reason a park never got a word
    /// of advice. This is that machine.
    ///
    /// ⚠ HE IS NOT A NOTIFICATION FEED, AND THE DIFFERENCE IS MOST OF THE FEEL. He arrives (50 ticks of
    /// rising and turning), says ONE thing for exactly as long as the recording runs, leaves (50), and is
    /// then away for 100 more. Statistics refresh in <see cref="AdvisorState.Idle"/> ALONE, so the whole
    /// park's sense of what is wrong with it stops while he talks and does not resume until he is back.
    /// Ticking the rules every frame instead -- which is the obvious way to wire this up, and what the port
    /// did for its first hour -- makes him a chatty overlay, and quietly runs the advice engine ~8 seconds
    /// per message faster than the console ever does.
    ///
    /// ⭐ HE NEVER REPEATS HIMSELF THE SAME WAY. Each message holds up to three separate recordings and a
    /// cursor that ROTATES THROUGH THEM, and starting to speak also rolls a gesture that is re-rolled until
    /// it differs from the last one (0x8001404C). Two thirds of the disc is his voice; this is what it is for.
    ///
    /// ⚠ ONE INFERENCE, MARKED. Where the queue's read cursor advances is not read from the binary: the
    /// dequeue at 0x80013E6C was not disassembled. It advances here when the message is delivered, which is
    /// the only placement that neither repeats nor drops one. Everything else in this file is read.</summary>
    public sealed class ParkAdvisor
    {
        /// <summary>READ 0x800131BC. The per-call elapsed tick count is clamped, so a long stall makes him
        /// finish an animation late rather than teleport through it.</summary>
        public const int MaxElapsed = 50;
        /// <summary>READ 0x800133F8 / 0x800134B0 / 0x80013558. Arriving and leaving are 50 ticks each.</summary>
        public const int TravelTicks = 50;
        /// <summary>READ 0x8001359C. And then he is away for 100.</summary>
        public const int GoneTicks = 100;
        /// <summary>READ 0x800133C4 + 0x800136F4. ⭐ THIS IS A SCALE, NOT A HEIGHT. The draw builds a
        /// matrix whose three diagonal entries are all `Scale >> 2`, so 1200 becomes 300/4096 = 0.073 --
        /// he GROWS FROM NOTHING over his 50 frames rather than rising into view. Named wrong here until
        /// fable traced the renderer; findings/advisor-presentation.md §1.2.</summary>
        public const int ScalePerFrame = 24, ScaleFull = 1200;
        /// <summary>READ 0x800133D0..F0 + 0x800C05B0. ⭐ AND THIS IS A SPIN IN THE SCREEN PLANE. The draw
        /// takes `Spin >> 1` as an angle in 4096ths of a turn about Z, so arriving runs it 0 -> 0x8000,
        /// i.e. 0 -> 0x4000 = FOUR FULL TURNS while he grows. Leaving unwinds them.</summary>
        public const int SpinPerFrame = 655, SpinArrived = 0x8000;
        /// <summary>READ 0x80013500. While talking the spin drifts towards a value re-rolled in this band
        /// each time it arrives. Through the >> 1 and the 0xFFF mask that is only ±100 of 4096, a lean of
        /// about ±8.8 degrees taking 200 frames to cross -- he sways, he does not turn.</summary>
        public const int SwayBase = 32568, SwaySpread = 400, SwaySlack = 5;
        /// <summary>READ 0x80014064. Five, and never the same one twice running.</summary>
        public const int GestureCount = 5;
        /// <summary>READ 0x80013D30. Twenty, oldest dropped when it overflows.</summary>
        public const int QueueSize = 20;
        /// <summary>READ 0x8001346C..A4. Completion hooks fire for these three after the recording ends.</summary>
        public const ushort CompletionMessage = 141, CompletionFirst = 221, CompletionLast = 288;

        public AdvisorFlags Flags { get; set; } = AdvisorFlags.Park;
        public AdvisorState State { get; private set; } = AdvisorState.Starting;
        /// <summary>READ +0x184. ⭐ IT IS HIS FACE. The draw uses it to pick a sub-mesh of FOLIO entry 0
        /// and attaches it to the body clip's first listed bone; 16 means draw no face at all. Latched
        /// from the take he is about to deliver, which is why every take of one message shares it.</summary>
        public byte Mood { get; private set; }
        /// <summary>READ +0x1B1. Which BODY clip he performs, re-rolled until it differs from the last.</summary>
        public byte Gesture { get; private set; }
        /// <summary>READ +0xE8 / +0xEC. His uniform scale and his spin about the screen's Z, in the
        /// original's raw units. See the constants: growing from nothing, spinning four times.</summary>
        public int Scale { get; private set; }
        public int Spin { get; private set; }
        /// <summary>READ +0xB6. The message he is delivering, or -1.</summary>
        public int Saying { get; private set; } = -1;
        /// <summary>Messages delivered since the park opened, for the readout.</summary>
        public int Delivered { get; private set; }

        public bool Idle => State == AdvisorState.Idle;
        public bool QueueEmpty => _write == _read;

        short _timer;
        int _sway;
        AdvisorPost? _urgent;
        readonly AdvisorPost[] _queue = new AdvisorPost[QueueSize];
        int _write, _read;
        /// <summary>⚠ The original's cursors live in the RAM copy of the message table, so they survive
        /// leaving a park and returning. These restart with the advisor. Within one park they behave
        /// identically; across two parks in one session the original is further through the rotation.</summary>
        readonly byte[] _take = new byte[AdvisorMessages.All.Length];

        /// <summary>READ 0x80013C1C. Ordinary messages queue; the last 68 ids interrupt him instead.</summary>
        public void Post(AdvisorPost post)
        {
            if (!AdvisorMessages.Has(post.Id)) return;
            if ((Flags & AdvisorFlags.Queue) != 0 && post.Id < AdvisorMessages.FirstInterrupt) Enqueue(post);
            else Interrupt(post);
        }

        public void Post(ushort id) => Post(new AdvisorPost(id));

        /// <summary>READ 0x80013D30. ⭐ DEDUPED BY ID: a park failing one rule for a week queues the
        /// complaint once, not once per pass. Overflow drops the oldest.</summary>
        void Enqueue(AdvisorPost post)
        {
            for (int i = _read; i != _write; i = (i + 1) % QueueSize)
                if (_queue[i].Id == post.Id) return;
            _queue[_write] = post;
            _write = (_write + 1) % QueueSize;
            if (_write == _read) _read = (_read + 1) % QueueSize;
        }

        /// <summary>READ 0x80013C80. He packs up mid-sentence and comes straight back with this one.</summary>
        void Interrupt(AdvisorPost post)
        {
            if (State == AdvisorState.Arriving || State == AdvisorState.Speaking)
            {
                if (State == AdvisorState.Speaking) _stopOnNextTick = true;
                _timer = TravelTicks;
                State = AdvisorState.Leaving;
            }
            _urgent = post;
        }

        bool _stopOnNextTick;

        /// <summary>One advisor update. <paramref name="elapsed"/> is ticks since the last one.
        /// READ 0x80013180: state 0 always advances by one, every other state by the clamped elapsed count.</summary>
        public void Tick(int elapsed, IParkAdvisorHost host, ParkStatistics statistics, IParkStatisticsWorld world)
        {
            if (host == null) return;
            int step = State == AdvisorState.Starting ? 1 : Math.Clamp(elapsed, 0, MaxElapsed);
            if (_stopOnNextTick) { _stopOnNextTick = false; host.StopVoice(); }

            switch (State)
            {
                case AdvisorState.Starting:
                    State = AdvisorState.Idle;
                    break;

                case AdvisorState.Idle:
                    if ((Flags & AdvisorFlags.Statistics) != 0 && statistics != null && world != null)
                        statistics.Tick(world);
                    if (_urgent is { } urgent) Begin(urgent.Id, host);
                    else if ((Flags & AdvisorFlags.Queue) != 0 && !QueueEmpty) Begin(_queue[_read].Id, host);
                    break;

                case AdvisorState.Arriving:
                    Scale += ScalePerFrame * step;
                    Spin += SpinPerFrame * step;
                    _timer -= (short)step;
                    if (_timer > 0) break;
                    Scale = ScaleFull;
                    Spin = SpinArrived;
                    Deliver(host);
                    _sway = SwayBase + host.Random(SwaySpread);
                    State = AdvisorState.Speaking;
                    break;

                case AdvisorState.Speaking:
                    if (!host.VoicePlaying)
                    {
                        _timer = TravelTicks;
                        State = AdvisorState.Leaving;
                        break;
                    }
                    if (Spin < _sway - SwaySlack) Spin += step;
                    else if (Spin > _sway + SwaySlack) Spin -= step;
                    else _sway = SwayBase + host.Random(SwaySpread);
                    break;

                case AdvisorState.Leaving:
                    Scale -= ScalePerFrame * step;
                    Spin -= SpinPerFrame * step;
                    _timer -= (short)step;
                    if (_timer > 0 || Scale >= 0) break;
                    host.HideCaption();
                    Saying = -1;
                    _timer = GoneTicks;
                    State = AdvisorState.Gone;
                    break;

                case AdvisorState.Gone:
                    _timer -= (short)step;
                    if (_timer <= 0 || (Flags & AdvisorFlags.Linger) == 0 || _urgent != null)
                    {
                        Scale = 0;
                        Spin = 0;
                        State = AdvisorState.Idle;
                    }
                    break;
            }
        }

        /// <summary>READ 0x800132C8. Starting to arrive latches the mood of the take he is ABOUT to give --
        /// the cursor does not move until he actually speaks -- and rolls a fresh gesture.</summary>
        void Begin(ushort id, IParkAdvisorHost host)
        {
            var record = AdvisorMessages.All[id];
            Mood = record.Mood(_take[id]);
            for (int i = 0; i < 8; i++)
            {
                byte roll = (byte)host.Random(GestureCount);
                if (roll != Gesture) { Gesture = roll; break; }
            }
            Scale = 0;
            Spin = 0;
            _timer = TravelTicks;
            State = AdvisorState.Arriving;
        }

        /// <summary>READ 0x80013EFC. The caption and the recording, and then the take rotates.</summary>
        void Deliver(IParkAdvisorHost host)
        {
            AdvisorPost post;
            if (_urgent is { } urgent) { _urgent = null; post = urgent; }
            else { post = _queue[_read]; _read = (_read + 1) % QueueSize; }

            Saying = post.Id;
            Delivered++;
            var record = AdvisorMessages.All[post.Id];
            if ((Flags & AdvisorFlags.Text) != 0 && record.TextId != AdvisorMessages.NoText)
                host.ShowCaption(record.TextId, post.Param, post.Value);
            if ((Flags & AdvisorFlags.Speech) == 0) return;
            if (host.VoicePlaying) host.StopVoice();
            int take = _take[post.Id];
            host.Speak(record.Line(take));
            _take[post.Id] = (byte)(take + 1 >= record.Count ? 0 : take + 1);
        }
    }
}
