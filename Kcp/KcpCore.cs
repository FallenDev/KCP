using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static System.Math;

using BufferOwner = System.Buffers.IMemoryOwner<byte>;

namespace System.Net.Sockets.Kcp
{
    public abstract class KcpConst
    {
        // In order to reduce the difficulty of reading, the variable names are as far as possible from the C version system
        /*
        conv session ID
        mtu maximum transmission unit
        mss maximum fragment size
        state Connection status (0xFFFFFFFF means disconnection)
        snd_una first unacknowledged packet
        snd_nxt the sequence number of the packet to be sent
        rcv_nxt message sequence number to be received
        ssthresh congestion window threshold
        rx_rttvar ack receives rtt float value
        rx_srtt ack receives rtt static value
        rx_rto is the recovery time calculated from the ack reception delay
        rx_minrto minimum recovery time
        snd_wnd send window size
        rcv_wnd receive window size
        rmt_wnd, remote receive window size
        cwnd, congestion window size
        probe Probe variable, IKCP_ASK_TELL means to inform the remote window size. IKCP_ASK_SEND means to request the remote end to inform the window size
        interval Internal flush refresh interval
        ts_flush The next flush refreshes the timestamp
        nodelay starts no-delay mode
        updated the update function has been called
        ts_probe, the timestamp of the next probe window
        probe_wait The time the probe window needs to wait
        dead_link maximum number of retransmissions
        incr the maximum amount of data that can be sent
        fastresend The number of repeated acks that trigger fast retransmission
        nocwnd cancel congestion control
        stream adopts streaming mode

        snd_queue send message queue
        rcv_queue queue to receive messages
        snd_buf buffer for sending messages
        rcv_buf buffer for receiving messages
        acklist ack list to be sent
        buffer memory for storing message byte streams
        The callback function of output udp sending message
        */

        #region Const

        public const int IKCP_RTO_NDL = 30;  // no delay min rto
        public const int IKCP_RTO_MIN = 100; // normal min rto
        public const int IKCP_RTO_DEF = 200;
        public const int IKCP_RTO_MAX = 60000;

        /// <summary>
        /// Datagram
        /// </summary>
        public const int IKCP_CMD_PUSH = 81; // cmd: push data
        /// <summary>
        /// Confirm message
        /// </summary>
        public const int IKCP_CMD_ACK = 82; // cmd: ack
        /// <summary>
        /// Window detection message, inquiring about the size of the remaining receiving window at the peer end
        /// </summary>
        public const int IKCP_CMD_WASK = 83; // cmd: window probe (ask)
        /// <summary>
        /// Window notification message, informing the peer of the size of the remaining receiving window
        /// </summary>
        public const int IKCP_CMD_WINS = 84; // cmd: window size (tell)
        /// <summary>
        /// IKCP_ASK_SEND means to request the remote end to inform the window size
        /// </summary>
        public const int IKCP_ASK_SEND = 1;  // need to send IKCP_CMD_WASK
        /// <summary>
        /// IKCP_ASK_TELL means to inform the remote window size
        /// </summary>
        public const int IKCP_ASK_TELL = 2;  // need to send IKCP_CMD_WINS
        public const int IKCP_WND_SND = 32;
        /// <summary>
        /// Receive window defaults. Must be greater than the maximum number of shards
        /// </summary>
        public const int IKCP_WND_RCV = 128; // must >= max fragment size
        /// <summary>
        /// Default maximum transmission unit Common routing value 1492 1480 Default 1400 to ensure that it will not be fragmented at the routing layer
        /// </summary>
        public const int IKCP_MTU_DEF = 1400;
        public const int IKCP_ACK_FAST = 3;
        public const int IKCP_INTERVAL = 100;
        public const int IKCP_OVERHEAD = 24;
        public const int IKCP_DEADLINK = 20;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN = 2;
        /// <summary>
        /// Windows Explorer CD
        /// </summary>
        public const int IKCP_PROBE_INIT = 7000;   // 7 secs to probe window size
        public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window
        public const int IKCP_FASTACK_LIMIT = 5;        // max times to trigger fastack

        #endregion

        /// <summary>
        /// <para>https://github.com/skywind3000/kcp/issues/53</para>
        /// Designed according to version C, using little-endian byte order
        /// </summary>
        public static bool IsLittleEndian = true;
    }

    /// <summary>
    /// https://luyuhuang.tech/2020/12/09/kcp.html
    /// https://github.com/skywind3000/kcp/wiki/Network-Layer
    /// <para>External buffer ----split copy----wait list -----move----send list----copy----send buffer---output</para>
    /// https://github.com/skywind3000/kcp/issues/118#issuecomment-338133930
    /// </summary>
    public partial class KcpCore<Segment> : KcpConst, IKcpSetting, IKcpUpdate, IDisposable
        where Segment : IKcpSegment
    {
        #region KCP members

        /// <summary>
        /// channel number
        /// </summary>
        public uint conv { get; protected set; }
        /// <summary>
        /// Maximum Transmission Unit，MTU
        /// </summary>
        protected uint mtu;

        /// <summary>
        /// buffer minimum size
        /// </summary>
        protected int BufferNeedSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)mtu;
        }

        /// <summary>
        /// Maximum Segment Length
        /// </summary>
        protected uint mss;
        /// <summary>
        /// Connection status (0xFFFFFFFF means disconnection)
        /// </summary>
        protected int state;
        /// <summary>
        /// first unacknowledged package
        /// </summary>
        protected uint snd_una;
        /// <summary>
        /// The sequence number of the packet to be sent
        /// </summary>
        protected uint snd_nxt;
        /// <summary>
        /// The ID of the next message to be received, the sequence number of the message to be received
        /// </summary>
        protected uint rcv_nxt;
        protected uint ts_recent;
        protected uint ts_lastack;
        /// <summary>
        /// Congestion window threshold
        /// </summary>
        protected uint ssthresh;
        /// <summary>
        /// ack receives rtt floating value
        /// </summary>
        protected uint rx_rttval;
        /// <summary>
        /// ack receives rtt static value
        /// </summary>
        protected uint rx_srtt;
        /// <summary>
        /// Recovery time calculated from ack reception delay. Retransmission TimeOut(RTO), timeout retransmission time.
        /// </summary>
        protected uint rx_rto;
        /// <summary>
        /// minimum recovery time
        /// </summary>
        protected uint rx_minrto;
        /// <summary>
        /// send window size
        /// </summary>
        protected uint snd_wnd;
        /// <summary>
        /// receive window size
        /// </summary>
        protected uint rcv_wnd;
        /// <summary>
        /// Remote receive window size
        /// </summary>
        protected uint rmt_wnd;
        /// <summary>
        /// congestion window size
        /// </summary>
        protected uint cwnd;
        /// <summary>
        /// Probing variables, IKCP_ASK_TELL means telling the remote window size. IKCP_ASK_SEND means to request the remote end to inform the window size
        /// </summary>
        protected uint probe;
        protected uint current;
        /// <summary>
        /// Internal flush refresh interval
        /// </summary>
        protected uint interval;
        /// <summary>
        /// The next flush refreshes the timestamp
        /// </summary>
        protected uint ts_flush;
        protected uint xmit;
        /// <summary>
        /// Whether to enable no delay mode
        /// </summary>
        protected uint nodelay;
        /// <summary>
        /// Whether the update function has been called
        /// </summary>
        protected uint updated;
        /// <summary>
        /// Timestamp of next probe window
        /// </summary>
        protected uint ts_probe;
        /// <summary>
        /// The amount of time the probe window needs to wait
        /// </summary>
        protected uint probe_wait;
        /// <summary>
        /// Maximum number of retransmissions
        /// </summary>
        protected uint dead_link;
        /// <summary>
        /// The maximum amount of data that can be sent
        /// </summary>
        protected uint incr;
        /// <summary>
        /// The number of repeated acks that trigger fast retransmission
        /// </summary>
        public int fastresend;
        public int fastlimit;
        /// <summary>
        /// cancel congestion control
        /// </summary>
        protected int nocwnd;
        protected int logmask;
        /// <summary>
        /// Whether to use streaming mode
        /// </summary>
        public int stream;
        protected BufferOwner buffer;

        #endregion

        #region Locks and Containers

        /// <summary>
        /// Add a lock to ensure the safety of the sending thread, otherwise the fragments of the two messages may be enqueued alternately.
        /// <para/> Use case: ordinary sending and broadcasting may cause multiple threads to call the Send method at the same time
        /// </summary>
        protected readonly object snd_queueLock = new();
        protected readonly object snd_bufLock = new();
        protected readonly object rcv_bufLock = new();
        protected readonly object rcv_queueLock = new();

        /// <summary>
        /// send ack queue
        /// </summary>
        protected ConcurrentQueue<(uint sn, uint ts)> acklist = new();
        /// <summary>
        /// send queue
        /// </summary>
        internal ConcurrentQueue<Segment> snd_queue = new();
        /// <summary>
        /// sending list
        /// </summary>
        internal LinkedList<Segment> snd_buf = new();
        /// <summary>
        /// Waiting to be triggered to receive a list of callback function messages
        /// <para>Action Required Add Traverse Delete</para>
        /// </summary>
        internal List<Segment> rcv_queue = new();
        /// <summary>
        /// Awaiting reorganization message list
        /// <para>Operations to be performed Add Insert Traverse Delete</para>
        /// </summary>
        internal LinkedList<Segment> rcv_buf = new();

        /// <summary>
        /// Get how many packet are waiting to be sent
        /// </summary>
        public int WaitSnd => snd_buf.Count + snd_queue.Count;

        #endregion

        public ISegmentManager<Segment> SegmentManager { get; set; }
        public KcpCore(uint conv_)
        {
            conv = conv_;

            snd_wnd = IKCP_WND_SND;
            rcv_wnd = IKCP_WND_RCV;
            rmt_wnd = IKCP_WND_RCV;
            mtu = IKCP_MTU_DEF;
            mss = mtu - IKCP_OVERHEAD;
            buffer = CreateBuffer(BufferNeedSize);

            rx_rto = IKCP_RTO_DEF;
            rx_minrto = IKCP_RTO_MIN;
            interval = IKCP_INTERVAL;
            ts_flush = IKCP_INTERVAL;
            ssthresh = IKCP_THRESH_INIT;
            fastlimit = IKCP_FASTACK_LIMIT;
            dead_link = IKCP_DEADLINK;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Is it being released
        /// </summary>
        private bool m_disposing = false;

        protected bool CheckDispose()
        {
            if (m_disposing)
            {
                return true;
            }

            if (disposedValue)
            {
                throw new ObjectDisposedException(
                    $"{nameof(Kcp)} [conv:{conv}]");
            }

            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                m_disposing = true;
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // Release managed state (managed object).
                        callbackHandle = null;
                        acklist = null;
                        buffer = null;
                    }

                    // Release unmanaged resources (unmanaged objects) and substitute finalizers in the following.
                    // Set large fields to null.
                    void FreeCollection(IEnumerable<Segment> collection)
                    {
                        if (collection == null)
                        {
                            return;
                        }
                        foreach (var item in collection)
                        {
                            try
                            {
                                SegmentManager.Free(item);
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }

                    lock (snd_queueLock)
                    {
                        while (snd_queue != null &&
                        (snd_queue.TryDequeue(out var segment)
                        || !snd_queue.IsEmpty)
                        )
                        {
                            try
                            {
                                SegmentManager.Free(segment);
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                        snd_queue = null;
                    }

                    lock (snd_bufLock)
                    {
                        FreeCollection(snd_buf);
                        snd_buf?.Clear();
                        snd_buf = null;
                    }

                    lock (rcv_bufLock)
                    {
                        FreeCollection(rcv_buf);
                        rcv_buf?.Clear();
                        rcv_buf = null;
                    }

                    lock (rcv_queueLock)
                    {
                        FreeCollection(rcv_queue);
                        rcv_queue?.Clear();
                        rcv_queue = null;
                    }


                    disposedValue = true;
                }
            }
            finally
            {
                m_disposing = false;
            }

        }

        // Dispose(bool disposing) above only replaces the finalizer if it has code for releasing unmanaged resources
        ~KcpCore()
        {
            // Do not change this code. Put the cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        /// <summary>
        /// Release is not strictly thread-safe, try to use the same thread call as Update,
        /// Or wait for it to be automatically released when it is destructed.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        protected IKcpCallback callbackHandle;
        private IKcpOutputWriter OutputWriter;

        private static uint Bound(uint lower, uint middle, uint upper) => Min(Max(lower, middle), upper);
        private static int TimeDiff(uint later, uint earlier) => (int)(later - earlier);
        protected virtual BufferOwner CreateBuffer(int needSize) => new KcpInnerBuffer(needSize);

        protected internal class KcpInnerBuffer : BufferOwner
        {
            private readonly Memory<byte> _memory;
            private bool _alreadyDisposed;

            public Memory<byte> Memory
            {
                get
                {
                    if (_alreadyDisposed)
                    {
                        throw new ObjectDisposedException(nameof(KcpInnerBuffer));
                    }

                    return _memory;
                }
            }

            public KcpInnerBuffer(int size)
            {
                _memory = new Memory<byte>(new byte[size]);
            }

            public void Dispose()
            {
                _alreadyDisposed = true;
            }
        }

        #region Functional logic

        /// <summary>
        /// Determine when should you invoke ikcp_update:
        /// returns when you should invoke ikcp_update in millisec, if there
        /// is no ikcp_input/_send calling. you can call ikcp_update in that
        /// time, instead of call update repeatly.
        /// Important to reduce unnecessary ikcp_update invoking. use it to
        /// schedule ikcp_update (eg. implementing an epoll-like mechanism,
        /// or optimize ikcp_update when handling massive kcp connections)
        /// </summary>
        public DateTimeOffset Check(in DateTimeOffset time)
        {
            if (CheckDispose())
            {
                return default;
            }

            if (updated == 0)
            {
                return time;
            }

            var current_ = time.ConvertTime();
            var ts_flush_ = ts_flush;
            var tm_packet = 0x7fffffff;

            if (TimeDiff(current_, ts_flush_) >= 10000 || TimeDiff(current_, ts_flush_) < -10000)
            {
                ts_flush_ = current_;
            }

            if (TimeDiff(current_, ts_flush_) >= 0)
            {
                return time;
            }

            var tm_flush_ = TimeDiff(ts_flush_, current_);

            lock (snd_bufLock)
            {
                foreach (var seg in snd_buf)
                {
                    var diff = TimeDiff(seg.resendts, current_);

                    if (diff <= 0)
                    {
                        return time;
                    }

                    if (diff < tm_packet)
                    {
                        tm_packet = diff;
                    }
                }
            }

            var minimal = tm_packet < tm_flush_ ? tm_packet : tm_flush_;
            if (minimal >= interval) minimal = (int)interval;
            return time + TimeSpan.FromMilliseconds(minimal);
        }

        /// <summary>
        /// Move available data from rcv_buf -> rcv_queue
        /// </summary>
        protected void Move_Rcv_buf_2_Rcv_queue()
        {
            lock (rcv_bufLock)
            {
                while (rcv_buf.Count > 0)
                {
                    var seg = rcv_buf.First!.Value;

                    if (seg.sn == rcv_nxt && rcv_queue.Count < rcv_wnd)
                    {
                        rcv_buf.RemoveFirst();

                        lock (rcv_queueLock)
                        {
                            rcv_queue.Add(seg);
                        }

                        rcv_nxt++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Update ack.
        /// </summary>
        private void Update_ack(int rtt)
        {
            if (rx_srtt == 0)
            {
                rx_srtt = (uint)rtt;
                rx_rttval = (uint)rtt / 2;
            }
            else
            {
                var delta = (int)((uint)rtt - rx_srtt);

                if (delta < 0)
                {
                    delta = -delta;
                }

                rx_rttval = (3 * rx_rttval + (uint)delta) / 4;
                rx_srtt = (uint)((7 * rx_srtt + rtt) / 8);

                if (rx_srtt < 1)
                {
                    rx_srtt = 1;
                }
            }

            var rto = rx_srtt + Max(interval, 4 * rx_rttval);
            rx_rto = Bound(rx_minrto, rto, IKCP_RTO_MAX);
        }

        private void Shrink_buf()
        {
            lock (snd_bufLock)
            {
                snd_una = snd_buf.Count > 0 ? snd_buf.First!.Value.sn : snd_nxt;
            }
        }

        private void Parse_ack(uint sn)
        {
            if (TimeDiff(sn, snd_una) < 0 || TimeDiff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (snd_bufLock)
            {
                for (var p = snd_buf.First; p != null; p = p.Next)
                {
                    var seg = p.Value;

                    if (sn == seg.sn)
                    {
                        snd_buf.Remove(p);
                        SegmentManager.Free(seg);
                        break;
                    }

                    if (TimeDiff(sn, seg.sn) < 0)
                    {
                        break;
                    }
                }
            }
        }

        private void Parse_una(uint una)
        {
            // Delete fragments older than a given time. keep the following fragment
            lock (snd_bufLock)
            {
                while (snd_buf.First != null)
                {
                    var seg = snd_buf.First.Value;

                    if (TimeDiff(una, seg.sn) > 0)
                    {
                        snd_buf.RemoveFirst();
                        SegmentManager.Free(seg);
                    }
                    else
                    {
                        break;
                    }
                }
            }

        }

        private void Parse_fastTrack(uint sn, uint ts)
        {
            if (TimeDiff(sn, snd_una) < 0 || TimeDiff(sn, snd_nxt) >= 0)
            {
                return;
            }

            lock (snd_bufLock)
            {
                foreach (var item in snd_buf)
                {
                    var seg = item;

                    if (TimeDiff(sn, seg.sn) < 0)
                    {
                        break;
                    }

                    if (sn != seg.sn)
                    {
#if !IKCP_FASTACK_CONSERVE
                        seg.fastack++;
#else
                        if (Itimediff(ts, seg.ts) >= 0)
                        {
                            seg.fastack++;
                        }
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Process packets received by the underlying network
        /// </summary>
        internal virtual void Parse_data(Segment newseg)
        {
            var sn = newseg.sn;

            lock (rcv_bufLock)
            {
                if (TimeDiff(sn, rcv_nxt + rcv_wnd) >= 0 || TimeDiff(sn, rcv_nxt) < 0)
                {
                    // If the received data packet number is greater than rcv_nxt + rcv_wnd or less than rcv_nxt, this packet will be discarded.
                    SegmentManager.Free(newseg);
                    return;
                }

                var repeat = false;

                // Check for duplicate messages and insertion positions
                LinkedListNode<Segment> p;
                for (p = rcv_buf.Last; p != null; p = p.Previous)
                {
                    var seg = p.Value;
                    if (seg.sn == sn)
                    {
                        repeat = true;
                        break;
                    }

                    if (TimeDiff(sn, seg.sn) > 0)
                    {
                        break;
                    }
                }

                if (!repeat)
                {
                    if (CanLog(KcpLogMask.IKCP_LOG_PARSE_DATA))
                    {
                        LogWriteLine($"{newseg.ToLogString()}", KcpLogMask.IKCP_LOG_PARSE_DATA.ToString());
                    }

                    if (p == null)
                    {
                        rcv_buf.AddFirst(newseg);
                        if (newseg.frg + 1 > rcv_wnd)
                        {
                            // The number of fragments is larger than the receiving window, causing kcp to block and freeze.
                            // Console.WriteLine($"The number of fragments is larger than the receiving window, causing kcp to block and freeze. frgCount:{newseg.frg + 1} rcv_wnd:{rcv_wnd}");
                            // 100% blocking freeze, printing logs is not necessary. Throw an exception directly.
                            throw new NotSupportedException($"The number of fragments is larger than the receiving window, causing KCP to block and freeze, frgCount:{newseg.frg + 1}  rcv_wnd:{rcv_wnd}");
                        }
                    }
                    else
                    {
                        rcv_buf.AddAfter(p, newseg);
                    }
                }
                else
                {
                    SegmentManager.Free(newseg);
                }
            }

            Move_Rcv_buf_2_Rcv_queue();
        }

        private ushort Wnd_unused()
        {
            // There is no lock here, so do not inline variables, otherwise it may cause inconsistency between the judgment variable and the assigned variable
            var waitCount = rcv_queue.Count;

            if (waitCount >= rcv_wnd) return 0;
            //Q: Why do you want to subtract nrcv_que, rcv_queue has already been sorted, and it should be counted in the receiving window, I feel incomprehensible?
            //The problem now is that if a large data packet has more fragments than the rcv_wnd receiving window, rcv_wnd will continue to be 0, blocking the entire process.
            //Personally, the data in rcv_queue is confirmed data, no matter whether the user is recv or not, it should not affect sending and receiving.
            //A: Now add a fragment number detection when sending out, if it is too large, an exception will be thrown directly. Prevent blocking sends.
            //Add a detection at the receiving end, if (frg+1) number of fragments > rcv_wnd, also throw an exception or warning. At least there is a hint.

            /// fix https://github.com/skywind3000/kcp/issues/126
            /// Actually rcv_wnd should not be greater than 65535
            var count = rcv_wnd - waitCount;
            return (ushort)Min(count, ushort.MaxValue);
        }

        /// <summary>
        /// Flush pending data
        /// </summary>
        private void Flush()
        {
            var current_ = current;
            var change = 0;
            var lost = 0;
            var offset = 0;

            if (updated == 0)
            {
                return;
            }

            var wnd_ = Wnd_unused();

            unsafe
            {
                // Allocate this segment on the stack, this segment will be destroyed as it is used, and will not be saved
                const int len = KcpSegment.LocalOffset + KcpSegment.HeadOffset;
                var ptr = stackalloc byte[len];
                var seg = new KcpSegment(ptr, 0)
                {
                    conv = conv,
                    cmd = IKCP_CMD_ACK,
                    wnd = wnd_,
                    una = rcv_nxt
                };

                #region Flush acknowledges

                if (CheckDispose())
                {
                    return;
                }

                while (acklist.TryDequeue(out var temp))
                {
                    if (offset + IKCP_OVERHEAD > mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }

                    seg.sn = temp.sn;
                    seg.ts = temp.ts;
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                #endregion

                #region Probe window size (if remote window size equals zero)

                if (rmt_wnd == 0)
                {
                    if (probe_wait == 0)
                    {
                        probe_wait = IKCP_PROBE_INIT;
                        ts_probe = current + probe_wait;
                    }
                    else
                    {
                        if (TimeDiff(current, ts_probe) >= 0)
                        {
                            if (probe_wait < IKCP_PROBE_INIT)
                            {
                                probe_wait = IKCP_PROBE_INIT;
                            }

                            probe_wait += probe_wait / 2;

                            if (probe_wait > IKCP_PROBE_LIMIT)
                            {
                                probe_wait = IKCP_PROBE_LIMIT;
                            }

                            ts_probe = current + probe_wait;
                            probe |= IKCP_ASK_SEND;
                        }
                    }
                }
                else
                {
                    ts_probe = 0;
                    probe_wait = 0;
                }

                #endregion

                #region Flush window probing commands

                if ((probe & IKCP_ASK_SEND) != 0)
                {
                    seg.cmd = IKCP_CMD_WASK;
                    if (offset + IKCP_OVERHEAD > (int)mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                if ((probe & IKCP_ASK_TELL) != 0)
                {
                    seg.cmd = IKCP_CMD_WINS;
                    if (offset + IKCP_OVERHEAD > (int)mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }
                    offset += seg.Encode(buffer.Memory.Span.Slice(offset));
                }

                probe = 0;

                #endregion
            }

            #region Refresh, move the sending waiting list to the sending list

            var cwnd_ = Min(snd_wnd, rmt_wnd);
            if (nocwnd == 0)
            {
                cwnd_ = Min(cwnd, cwnd_);
            }

            while (TimeDiff(snd_nxt, snd_una + cwnd_) < 0)
            {
                if (snd_queue.TryDequeue(out var newseg))
                {
                    newseg.conv = conv;
                    newseg.cmd = IKCP_CMD_PUSH;
                    newseg.wnd = wnd_;
                    newseg.ts = current_;
                    newseg.sn = snd_nxt;
                    snd_nxt++;
                    newseg.una = rcv_nxt;
                    newseg.resendts = current_;
                    newseg.rto = rx_rto;
                    newseg.fastack = 0;
                    newseg.xmit = 0;
                    lock (snd_bufLock)
                    {
                        snd_buf.AddLast(newseg);
                    }
                }
                else
                {
                    break;
                }
            }

            #endregion

            #region Refresh send list, call Output

            var resent = fastresend > 0 ? (uint)fastresend : 0xffffffff;
            var rtomin = nodelay == 0 ? (rx_rto >> 3) : 0;

            lock (snd_bufLock)
            {
                foreach (var item in snd_buf)
                {
                    var segment = item;
                    var needsend = false;
                    if (segment.xmit == 0)
                    {
                        // Newly added to snd_buf, the message that has never been sent is sent directly;
                        needsend = true;
                        segment.xmit++;
                        segment.rto = rx_rto;
                        segment.resendts = current_ + rx_rto + rtomin;
                    }
                    else if (TimeDiff(current_, segment.resendts) >= 0)
                    {
                        // A message that has been sent but has not received an ACK within the RTO needs to be retransmitted;
                        needsend = true;
                        segment.xmit++;
                        xmit++;
                        if (nodelay == 0)
                        {
                            segment.rto += Max(segment.rto, rx_rto);
                        }
                        else
                        {
                            var step = nodelay < 2 ? segment.rto : rx_rto;
                            segment.rto += step / 2;
                        }

                        segment.resendts = current_ + segment.rto;
                        lost = 1;
                    }
                    else if (segment.fastack >= resent)
                    {
                        // Packets that have been sent, but the ACKs are out of order several times, need to perform fast retransmission.
                        if (segment.xmit <= fastlimit
                            || fastlimit <= 0)
                        {
                            needsend = true;
                            segment.xmit++;
                            segment.fastack = 0;
                            segment.resendts = current_ + segment.rto;
                            change++;
                        }
                    }

                    if (!needsend) continue;
                    segment.ts = current_;
                    segment.wnd = wnd_;
                    segment.una = rcv_nxt;

                    var need = IKCP_OVERHEAD + segment.len;
                    if (offset + need > mtu)
                    {
                        callbackHandle.Output(buffer, offset);
                        offset = 0;
                        buffer = CreateBuffer(BufferNeedSize);
                    }

                    offset += segment.Encode(buffer.Memory.Span.Slice(offset));

                    if (CanLog(KcpLogMask.IKCP_LOG_NEED_SEND))
                    {
                        LogWriteLine($"{segment.ToLogString(true)}", KcpLogMask.IKCP_LOG_NEED_SEND.ToString());
                    }

                    if (segment.xmit < dead_link) continue;
                    state = -1;

                    if (CanLog(KcpLogMask.IKCP_LOG_DEAD_LINK))
                    {
                        LogWriteLine($"state = -1; xmit:{segment.xmit} >= dead_link:{dead_link}", KcpLogMask.IKCP_LOG_DEAD_LINK.ToString());
                    }
                }
            }

            // Flash remain segments
            if (offset > 0)
            {
                callbackHandle.Output(buffer, offset);
                offset = 0;
                buffer = CreateBuffer(BufferNeedSize);
            }

            #endregion

            #region Update ssthresh

            if (change != 0)
            {
                var inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            if (lost != 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

                cwnd = 1;
                incr = mss;
            }

            if (cwnd >= 1) return;
            cwnd = 1;
            incr = mss;

            #endregion
        }

        protected void Flush2()
        {
            var current_ = current;
            var change = 0;
            var lost = 0;

            if (updated == 0)
            {
                return;
            }

            var wnd_ = Wnd_unused();

            unsafe
            {
                // Allocate this segment on the stack, this segment will be destroyed as it is used, and will not be saved
                const int len = KcpSegment.LocalOffset + KcpSegment.HeadOffset;
                var ptr = stackalloc byte[len];
                var seg = new KcpSegment(ptr, 0)
                {
                    conv = conv,
                    cmd = IKCP_CMD_ACK,
                    wnd = wnd_,
                    una = rcv_nxt
                };

                #region Flush Acknowledges

                if (CheckDispose())
                {
                    return;
                }

                while (acklist.TryDequeue(out var temp))
                {
                    if (OutputWriter.UnflushedBytes + IKCP_OVERHEAD > mtu)
                    {
                        OutputWriter.Flush();
                    }

                    seg.sn = temp.sn;
                    seg.ts = temp.ts;
                    seg.Encode(OutputWriter);
                }

                #endregion

                #region Probe window size (if remote window size equals zero)

                if (rmt_wnd == 0)
                {
                    if (probe_wait == 0)
                    {
                        probe_wait = IKCP_PROBE_INIT;
                        ts_probe = current + probe_wait;
                    }
                    else
                    {
                        if (TimeDiff(current, ts_probe) >= 0)
                        {
                            if (probe_wait < IKCP_PROBE_INIT)
                            {
                                probe_wait = IKCP_PROBE_INIT;
                            }

                            probe_wait += probe_wait / 2;

                            if (probe_wait > IKCP_PROBE_LIMIT)
                            {
                                probe_wait = IKCP_PROBE_LIMIT;
                            }

                            ts_probe = current + probe_wait;
                            probe |= IKCP_ASK_SEND;
                        }
                    }
                }
                else
                {
                    ts_probe = 0;
                    probe_wait = 0;
                }

                #endregion

                #region Flush window probing commands

                if ((probe & IKCP_ASK_SEND) != 0)
                {
                    seg.cmd = IKCP_CMD_WASK;
                    if (OutputWriter.UnflushedBytes + IKCP_OVERHEAD > (int)mtu)
                    {
                        OutputWriter.Flush();
                    }
                    seg.Encode(OutputWriter);
                }

                if ((probe & IKCP_ASK_TELL) != 0)
                {
                    seg.cmd = IKCP_CMD_WINS;
                    if (OutputWriter.UnflushedBytes + IKCP_OVERHEAD > (int)mtu)
                    {
                        OutputWriter.Flush();
                    }
                    seg.Encode(OutputWriter);
                }

                probe = 0;

                #endregion
            }

            #region Refresh, move the sending waiting list to the sending list

            var cwnd_ = Min(snd_wnd, rmt_wnd);
            if (nocwnd == 0)
            {
                cwnd_ = Min(cwnd, cwnd_);
            }

            while (TimeDiff(snd_nxt, snd_una + cwnd_) < 0)
            {
                if (snd_queue.TryDequeue(out var newseg))
                {
                    newseg.conv = conv;
                    newseg.cmd = IKCP_CMD_PUSH;
                    newseg.wnd = wnd_;
                    newseg.ts = current_;
                    newseg.sn = snd_nxt;
                    snd_nxt++;
                    newseg.una = rcv_nxt;
                    newseg.resendts = current_;
                    newseg.rto = rx_rto;
                    newseg.fastack = 0;
                    newseg.xmit = 0;
                    lock (snd_bufLock)
                    {
                        snd_buf.AddLast(newseg);
                    }
                }
                else
                {
                    break;
                }
            }

            #endregion

            #region Refresh send list, call Output

            var resent = fastresend > 0 ? (uint)fastresend : 0xffffffff;
            var rtomin = nodelay == 0 ? (rx_rto >> 3) : 0;

            lock (snd_bufLock)
            {
                // Flush data segments
                foreach (var item in snd_buf)
                {
                    var segment = item;
                    var needsend = false;
                    if (segment.xmit == 0)
                    {
                        // Newly added to snd_buf, the message that has never been sent is sent directly;
                        needsend = true;
                        segment.xmit++;
                        segment.rto = rx_rto;
                        segment.resendts = current_ + rx_rto + rtomin;
                    }
                    else if (TimeDiff(current_, segment.resendts) >= 0)
                    {
                        // A message that has been sent but has not received an ACK within the RTO needs to be retransmitted;
                        needsend = true;
                        segment.xmit++;
                        xmit++;
                        if (nodelay == 0)
                        {
                            segment.rto += Max(segment.rto, rx_rto);
                        }
                        else
                        {
                            var step = nodelay < 2 ? segment.rto : rx_rto;
                            segment.rto += step / 2;
                        }

                        segment.resendts = current_ + segment.rto;
                        lost = 1;
                    }
                    else if (segment.fastack >= resent)
                    {
                        // Packets that have been sent, but the ACKs are out of order several times, need to perform fast retransmission.
                        if (segment.xmit <= fastlimit
                            || fastlimit <= 0)
                        {
                            needsend = true;
                            segment.xmit++;
                            segment.fastack = 0;
                            segment.resendts = current_ + segment.rto;
                            change++;
                        }
                    }

                    if (needsend)
                    {
                        segment.ts = current_;
                        segment.wnd = wnd_;
                        segment.una = rcv_nxt;

                        var need = IKCP_OVERHEAD + segment.len;
                        if (OutputWriter.UnflushedBytes + need > mtu)
                        {
                            OutputWriter.Flush();
                        }

                        segment.Encode(OutputWriter);

                        if (CanLog(KcpLogMask.IKCP_LOG_NEED_SEND))
                        {
                            LogWriteLine($"{segment.ToLogString(true)}", KcpLogMask.IKCP_LOG_NEED_SEND.ToString());
                        }

                        if (segment.xmit >= dead_link)
                        {
                            state = -1;

                            if (CanLog(KcpLogMask.IKCP_LOG_DEAD_LINK))
                            {
                                LogWriteLine($"state = -1; xmit:{segment.xmit} >= dead_link:{dead_link}", KcpLogMask.IKCP_LOG_DEAD_LINK.ToString());
                            }
                        }
                    }
                }
            }


            // Flash remain segments
            if (OutputWriter.UnflushedBytes > 0)
            {
                OutputWriter.Flush();
            }

            #endregion

            #region update ssthresh

            // Update ssthresh calculates ssthresh and cwnd based on packet loss.
            if (change != 0)
            {
                var inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            if (lost != 0)
            {
                ssthresh = cwnd / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                {
                    ssthresh = IKCP_THRESH_MIN;
                }

                cwnd = 1;
                incr = mss;
            }

            if (cwnd >= 1) return;
            cwnd = 1;
            incr = mss;

            #endregion

        }

        /// <summary>
        /// update state (call it repeatedly, every 10ms-100ms), or you can ask
        /// ikcp_check when to call it again (without ikcp_input/_send calling).
        /// </summary>
        /// <param name="time">DateTime.UtcNow</param>
        public void Update(in DateTimeOffset time)
        {
            if (CheckDispose())
            {
                return;
            }

            current = time.ConvertTime();

            if (updated == 0)
            {
                updated = 1;
                ts_flush = current;
            }

            var slap = TimeDiff(current, ts_flush);

            if (slap >= 10000 || slap < -10000)
            {
                ts_flush = current;
                slap = 0;
            }

            if (slap >= 0)
            {
                ts_flush += interval;
                if (TimeDiff(current, ts_flush) >= 0)
                {
                    ts_flush = current + interval;
                }

                Flush();
            }
        }

        #endregion

        #region Setting control

        public int SetMtu(int mtu = IKCP_MTU_DEF)
        {
            if (mtu is < 50 or < IKCP_OVERHEAD)
            {
                return -1;
            }

            var buffer_ = CreateBuffer(BufferNeedSize);

            if (null == buffer_)
            {
                return -2;
            }

            this.mtu = (uint)mtu;
            mss = this.mtu - IKCP_OVERHEAD;
            buffer.Dispose();
            buffer = buffer_;
            return 0;
        }

        public int Interval(int interval_)
        {
            interval_ = interval_ switch
            {
                > 5000 => 5000,
                < 0 => 0,
                _ => interval_
            };

            interval = (uint)interval_;
            return 0;
        }

        public int NoDelay(int nodelay_, int interval_, int resend_, int nc_)
        {

            if (nodelay_ > 0)
            {
                nodelay = (uint)nodelay_;
                rx_minrto = IKCP_RTO_NDL;
            }

            if (resend_ >= 0)
            {
                fastresend = resend_;
            }

            if (nc_ >= 0)
            {
                nocwnd = nc_;
            }

            return Interval(interval_);
        }

        public int WndSize(int sndwnd = IKCP_WND_SND, int rcvwnd = IKCP_WND_RCV)
        {
            if (sndwnd > 0)
            {
                snd_wnd = (uint)sndwnd;
            }

            if (rcvwnd > 0)
            {
                rcv_wnd = (uint)rcvwnd;
            }

            return 0;
        }

        #endregion
    }

    public partial class KcpCore<Segment> : IKcpSendable
    {
        /// <summary>
        /// User/Upper Level send, returns below zero for error
        /// </summary>
        public int Send(ReadOnlySpan<byte> span, object options = null)
        {
            if (CheckDispose())
            {
                return -4;
            }

            if (mss <= 0)
            {
                throw new InvalidOperationException($" mss <= 0 ");
            }

            if (span.Length == 0)
            {
                return -1;
            }

            var offset = 0;
            int count;

            #region Fragment

            if (span.Length <= mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(span.Length + mss - 1) / (int)mss;
            }

            switch (count)
            {
                case > IKCP_WND_RCV:
                    return -2;
                case 0:
                    count = 1;
                    break;
            }

            lock (snd_queueLock)
            {
                for (var i = 0; i < count; i++)
                {
                    int size;
                    if (span.Length - offset > mss)
                    {
                        size = (int)mss;
                    }
                    else
                    {
                        size = span.Length - offset;
                    }

                    var seg = SegmentManager.Alloc(size);
                    span.Slice(offset, size).CopyTo(seg.data);
                    offset += size;
                    seg.frg = (byte)(count - i - 1);
                    snd_queue.Enqueue(seg);
                }
            }

            #endregion

            return 0;
        }

        public int Send(ReadOnlySequence<byte> span, object options = null)
        {
            if (CheckDispose())
            {
                return -4;
            }

            if (mss <= 0)
            {
                throw new InvalidOperationException($" mss <= 0 ");
            }

            if (span.Length == 0)
            {
                return -1;
            }

            var offset = 0;
            int count;

            #region Fragment

            if (span.Length <= mss)
            {
                count = 1;
            }
            else
            {
                count = (int)(span.Length + mss - 1) / (int)mss;
            }

            switch (count)
            {
                case > IKCP_WND_RCV:
                    return -2;
                case 0:
                    count = 1;
                    break;
            }

            lock (snd_queueLock)
            {
                for (var i = 0; i < count; i++)
                {
                    int size;
                    if (span.Length - offset > mss)
                    {
                        size = (int)mss;
                    }
                    else
                    {
                        size = (int)span.Length - offset;
                    }

                    var seg = SegmentManager.Alloc(size);
                    span.Slice(offset, size).CopyTo(seg.data);
                    offset += size;
                    seg.frg = (byte)(count - i - 1);
                    snd_queue.Enqueue(seg);
                }
            }

            #endregion

            return 0;
        }
    }

    public partial class KcpCore<Segment> : IKcpInputable
    {
        /// <summary>
        /// When you receive a low level packet (eg. UDP packet), call it
        /// </summary>
        public int Input(ReadOnlySpan<byte> span)
        {
            if (CheckDispose())
            {
                return -4;
            }

            if (CanLog(KcpLogMask.IKCP_LOG_INPUT))
            {
                LogWriteLine($"[RI] {span.Length} bytes", KcpLogMask.IKCP_LOG_INPUT.ToString());
            }

            if (span.Length < IKCP_OVERHEAD)
            {
                return -1;
            }

            var prev_una = snd_una;
            var offset = 0;
            var flag = 0;
            uint maxack = 0;
            uint latest_ts = 0;

            while (true)
            {
                uint ts = 0;
                uint sn = 0;
                uint length = 0;
                uint una = 0;
                uint conv_ = 0;
                ushort wnd = 0;
                byte cmd = 0;
                byte frg = 0;

                if (span.Length - offset < IKCP_OVERHEAD)
                {
                    break;
                }

                Span<byte> header = stackalloc byte[24];
                span.Slice(offset, 24).CopyTo(header);
                offset += ReadHeader(header,
                                     ref conv_,
                                     ref cmd,
                                     ref frg,
                                     ref wnd,
                                     ref ts,
                                     ref sn,
                                     ref una,
                                     ref length);

                if (conv != conv_)
                {
                    return -1;
                }

                if (span.Length - offset < length || (int)length < 0)
                {
                    return -2;
                }

                switch (cmd)
                {
                    case IKCP_CMD_PUSH:
                    case IKCP_CMD_ACK:
                    case IKCP_CMD_WASK:
                    case IKCP_CMD_WINS:
                        break;
                    default:
                        return -3;
                }

                rmt_wnd = wnd;
                Parse_una(una);
                Shrink_buf();

                switch (cmd)
                {
                    case IKCP_CMD_ACK:
                    {
                        if (TimeDiff(current, ts) >= 0)
                        {
                            Update_ack(TimeDiff(current, ts));
                        }

                        Parse_ack(sn);
                        Shrink_buf();

                        if (flag == 0)
                        {
                            flag = 1;
                            maxack = sn;
                            latest_ts = ts;
                        }
                        else if (TimeDiff(sn, maxack) > 0)
                        {
#if !IKCP_FASTACK_CONSERVE
                            maxack = sn;
                            latest_ts = ts;
#else
                        if (Itimediff(ts, latest_ts) > 0)
                        {
                            maxack = sn;
                            latest_ts = ts;
                        }
#endif
                        }

                        if (CanLog(KcpLogMask.IKCP_LOG_IN_ACK))
                        {
                            LogWriteLine($"input ack: sn={sn} rtt={TimeDiff(current, ts)} rto={rx_rto}", KcpLogMask.IKCP_LOG_IN_ACK.ToString());
                        }

                        break;
                    }
                    case IKCP_CMD_PUSH:
                    {
                        if (CanLog(KcpLogMask.IKCP_LOG_IN_DATA))
                        {
                            LogWriteLine($"input psh: sn={sn} ts={ts}", KcpLogMask.IKCP_LOG_IN_DATA.ToString());
                        }

                        if (TimeDiff(sn, rcv_nxt + rcv_wnd) < 0)
                        {
                            // Instead of ikcp_ack_push
                            acklist.Enqueue((sn, ts));

                            if (TimeDiff(sn, rcv_nxt) >= 0)
                            {
                                var seg = SegmentManager.Alloc((int)length);
                                seg.conv = conv_;
                                seg.cmd = cmd;
                                seg.frg = frg;
                                seg.wnd = wnd;
                                seg.ts = ts;
                                seg.sn = sn;
                                seg.una = una;

                                if (length > 0)
                                {
                                    span.Slice(offset, (int)length).CopyTo(seg.data);
                                }

                                Parse_data(seg);
                            }
                        }

                        break;
                    }
                    case IKCP_CMD_WASK:
                    {
                        // Ready to send back IKCP_CMD_WINS in Ikcp_flush
                        // Tell remote my window size
                        probe |= IKCP_ASK_TELL;

                        if (CanLog(KcpLogMask.IKCP_LOG_IN_PROBE))
                        {
                            LogWriteLine($"input probe", KcpLogMask.IKCP_LOG_IN_PROBE.ToString());
                        }

                        break;
                    }
                    case IKCP_CMD_WINS:
                    {
                        // Do nothing
                        if (CanLog(KcpLogMask.IKCP_LOG_IN_WINS))
                        {
                            LogWriteLine($"input wins: {wnd}", KcpLogMask.IKCP_LOG_IN_WINS.ToString());
                        }

                        break;
                    }
                }

                offset += (int)length;
            }

            if (flag != 0)
            {
                Parse_fastTrack(maxack, latest_ts);
            }

            if (TimeDiff(snd_una, prev_una) <= 0) return 0;
            if (cwnd >= rmt_wnd) return 0;

            if (cwnd < ssthresh)
            {
                cwnd++;
                incr += mss;
            }
            else
            {
                if (incr < mss)
                {
                    incr = mss;
                }
                incr += (mss * mss) / incr + (mss / 16);
                if ((cwnd + 1) * mss <= incr)
                {
#if true
                    cwnd = (incr + mss - 1) / ((mss > 0) ? mss : 1);
#else
                            cwnd++;
#endif
                }
            }

            if (cwnd <= rmt_wnd) return 0;
            cwnd = rmt_wnd;
            incr = rmt_wnd * mss;

            return 0;
        }

        /// <summary>
        /// <inheritdoc cref="Input(ReadOnlySpan{byte})"/>
        /// </summary>
        public int Input(ReadOnlySequence<byte> span)
        {
            if (CheckDispose())
            {
                return -4;
            }

            if (CanLog(KcpLogMask.IKCP_LOG_INPUT))
            {
                LogWriteLine($"[RI] {span.Length} bytes", KcpLogMask.IKCP_LOG_INPUT.ToString());
            }

            if (span.Length < IKCP_OVERHEAD)
            {
                return -1;
            }

            var prev_una = snd_una;
            var offset = 0;
            var flag = 0;
            uint maxack = 0;
            uint latest_ts = 0;

            while (true)
            {
                uint ts = 0;
                uint sn = 0;
                uint length = 0;
                uint una = 0;
                uint conv_ = 0;
                ushort wnd = 0;
                byte cmd = 0;
                byte frg = 0;

                if (span.Length - offset < IKCP_OVERHEAD) break;

                Span<byte> header = stackalloc byte[24];
                span.Slice(offset, 24).CopyTo(header);
                offset += ReadHeader(header,
                                     ref conv_,
                                     ref cmd,
                                     ref frg,
                                     ref wnd,
                                     ref ts,
                                     ref sn,
                                     ref una,
                                     ref length);

                if (conv != conv_)
                {
                    return -1;
                }

                if (span.Length - offset < length || (int)length < 0)
                {
                    return -2;
                }

                switch (cmd)
                {
                    case IKCP_CMD_PUSH:
                    case IKCP_CMD_ACK:
                    case IKCP_CMD_WASK:
                    case IKCP_CMD_WINS:
                        break;
                    default:
                        return -3;
                }

                rmt_wnd = wnd;
                Parse_una(una);
                Shrink_buf();

                switch (cmd)
                {
                    case IKCP_CMD_ACK:
                    {
                        if (TimeDiff(current, ts) >= 0)
                        {
                            Update_ack(TimeDiff(current, ts));
                        }

                        Parse_ack(sn);
                        Shrink_buf();

                        if (flag == 0)
                        {
                            flag = 1;
                            maxack = sn;
                            latest_ts = ts;
                        }
                        else if (TimeDiff(sn, maxack) > 0)
                        {
#if !IKCP_FASTACK_CONSERVE
                            maxack = sn;
                            latest_ts = ts;
#else
                        if (Itimediff(ts, latest_ts) > 0)
                        {
                            maxack = sn;
                            latest_ts = ts;
                        }
#endif
                        }

                        if (CanLog(KcpLogMask.IKCP_LOG_IN_ACK))
                        {
                            LogWriteLine($"input ack: sn={sn} rtt={TimeDiff(current, ts)} rto={rx_rto}", KcpLogMask.IKCP_LOG_IN_ACK.ToString());
                        }

                        break;
                    }
                    case IKCP_CMD_PUSH:
                    {
                        if (CanLog(KcpLogMask.IKCP_LOG_IN_DATA))
                        {
                            LogWriteLine($"input psh: sn={sn} ts={ts}", KcpLogMask.IKCP_LOG_IN_DATA.ToString());
                        }

                        if (TimeDiff(sn, rcv_nxt + rcv_wnd) < 0)
                        {
                            // Instead of ikcp_ack_push
                            acklist.Enqueue((sn, ts));

                            if (TimeDiff(sn, rcv_nxt) >= 0)
                            {
                                var seg = SegmentManager.Alloc((int)length);
                                seg.conv = conv_;
                                seg.cmd = cmd;
                                seg.frg = frg;
                                seg.wnd = wnd;
                                seg.ts = ts;
                                seg.sn = sn;
                                seg.una = una;

                                if (length > 0)
                                {
                                    span.Slice(offset, (int)length).CopyTo(seg.data);
                                }

                                Parse_data(seg);
                            }
                        }

                        break;
                    }
                    case IKCP_CMD_WASK:
                    {
                        // Ready to send back IKCP_CMD_WINS in Ikcp_flush
                        // Tell remote my window size
                        probe |= IKCP_ASK_TELL;

                        if (CanLog(KcpLogMask.IKCP_LOG_IN_PROBE))
                        {
                            LogWriteLine($"input probe:", KcpLogMask.IKCP_LOG_IN_PROBE.ToString());
                        }

                        break;
                    }
                    case IKCP_CMD_WINS:
                    {
                        // Do nothing
                        if (CanLog(KcpLogMask.IKCP_LOG_IN_WINS))
                        {
                            LogWriteLine($"input wins: {wnd}", KcpLogMask.IKCP_LOG_IN_WINS.ToString());
                        }

                        break;
                    }
                }

                offset += (int)length;
            }

            if (flag != 0)
            {
                Parse_fastTrack(maxack, latest_ts);
            }

            if (TimeDiff(snd_una, prev_una) <= 0) return 0;
            if (cwnd >= rmt_wnd) return 0;

            if (cwnd < ssthresh)
            {
                cwnd++;
                incr += mss;
            }
            else
            {
                if (incr < mss)
                {
                    incr = mss;
                }

                incr += (mss * mss) / incr + (mss / 16);

                if ((cwnd + 1) * mss <= incr)
                {
#if true
                    cwnd = (incr + mss - 1) / ((mss > 0) ? mss : 1);
#else
                            cwnd++;
#endif
                }
            }

            if (cwnd <= rmt_wnd) return 0;
            cwnd = rmt_wnd;
            incr = rmt_wnd * mss;

            return 0;
        }

        public static int ReadHeader(ReadOnlySpan<byte> header,
                              ref uint conv_,
                              ref byte cmd,
                              ref byte frg,
                              ref ushort wnd,
                              ref uint ts,
                              ref uint sn,
                              ref uint una,
                              ref uint length)
        {
            var offset = 0;

            if (IsLittleEndian)
            {
                conv_ = BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                offset += 4;

                cmd = header[offset];
                offset += 1;
                frg = header[offset];
                offset += 1;
                wnd = BinaryPrimitives.ReadUInt16LittleEndian(header[offset..]);
                offset += 2;

                ts = BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                offset += 4;
                sn = BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                offset += 4;
                una = BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                offset += 4;
                length = BinaryPrimitives.ReadUInt32LittleEndian(header[offset..]);
                offset += 4;
            }
            else
            {
                conv_ = BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
                offset += 4;
                cmd = header[offset];
                offset += 1;
                frg = header[offset];
                offset += 1;
                wnd = BinaryPrimitives.ReadUInt16BigEndian(header[offset..]);
                offset += 2;

                ts = BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
                offset += 4;
                sn = BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
                offset += 4;
                una = BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
                offset += 4;
                length = BinaryPrimitives.ReadUInt32BigEndian(header[offset..]);
                offset += 4;
            }

            return offset;
        }
    }
}
