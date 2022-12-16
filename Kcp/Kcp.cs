using System.Buffers;

using BufferOwner = System.Buffers.IMemoryOwner<byte>;

namespace System.Net.Sockets.Kcp
{
    public class Kcp<Segment> : KcpCore<Segment> where Segment : IKcpSegment
    {
        /// <summary>
        /// Create a new KCP control object, 'conv' must equal in two endpoint
        /// from the same connection.
        /// </summary>
        public Kcp(uint conv_, IKcpCallback callback, IRentable rentable = null) : base(conv_)
        {
            callbackHandle = callback;
            this.rentable = rentable;
        }

        IRentable rentable;

        /// <summary>
        /// If the external buffer can be provided, use the external buffer, otherwise new byte[]
        /// </summary>
        protected internal override BufferOwner CreateBuffer(int needSize)
        {
            var res = rentable?.RentBuffer(needSize);
            if (res == null)
            {
                return base.CreateBuffer(needSize);
            }

            if (res.Memory.Length < needSize)
            {
                throw new ArgumentException($"{nameof(rentable.RentBuffer)} The specified delegate does not meet the criteria returned" +
                                            $"BufferOwner.Memory.Length less than {nameof(needSize)}");
            }

            return res;
        }

        /// <summary>
        /// TryRecv Recv is designed to only allow one thread to call at a time.
        /// </summary>
        public (BufferOwner buffer, int avalidLength) TryRecv()
        {
            var peekSize = -1;
            lock (rcv_queueLock)
            {
                if (rcv_queue.Count == 0)
                {
                    // No package available
                    return (null, -1);
                }

                var seq = rcv_queue[0];

                if (seq.frg == 0)
                {
                    peekSize = (int)seq.len;
                }

                if (rcv_queue.Count < seq.frg + 1)
                {
                    // Not enough packages
                    return (null, -1);
                }

                uint length = 0;

                foreach (var item in rcv_queue)
                {
                    length += item.len;
                    if (item.frg == 0) break;
                }

                peekSize = (int)length;

                if (peekSize <= 0)
                {
                    return (null, -2);
                }
            }

            var buffer = CreateBuffer(peekSize);
            var recvlength = UncheckRecv(buffer.Memory.Span);
            return (buffer, recvlength);
        }

        /// <summary>
        /// TryRecv Recv is designed to only allow one thread to call at a time.
        /// </summary>
        public int TryRecv(IBufferWriter<byte> writer)
        {
            var peekSize = -1;
            lock (rcv_queueLock)
            {
                if (rcv_queue.Count == 0)
                {
                    // No package available
                    return -1;
                }

                var seq = rcv_queue[0];

                if (seq.frg == 0)
                {
                    peekSize = (int)seq.len;
                }

                if (rcv_queue.Count < seq.frg + 1)
                {
                    // Not enough packages
                    return -1;
                }

                uint length = 0;

                foreach (var item in rcv_queue)
                {
                    length += item.len;
                    if (item.frg == 0) break;
                }

                peekSize = (int)length;

                if (peekSize <= 0)
                {
                    return -2;
                }
            }

            return UncheckRecv(writer);
        }

        /// <summary>
        /// User/Upper Level recv: returns size, returns below zero for EAGAIN
        /// </summary>
        public int Recv(Span<byte> buffer)
        {
            lock (rcv_queueLock)
            {
                if (0 == rcv_queue.Count)
                {
                    return -1;
                }

                var peekSize = PeekSize();
                if (peekSize < 0)
                {
                    return -2;
                }

                if (peekSize > buffer.Length)
                {
                    return -3;
                }
            }

            // Split function
            var recvLength = UncheckRecv(buffer);
            return recvLength;
        }

        /// <summary>
        /// User/Upper Level recv: returns size, returns below zero for EAGAIN
        /// </summary>
        public int Recv(IBufferWriter<byte> writer)
        {
            lock (rcv_queueLock)
            {
                if (0 == rcv_queue.Count)
                {
                    return -1;
                }

                var peekSize = PeekSize();
                if (peekSize < 0)
                {
                    return -2;
                }
            }

            // Split function
            var recvLength = UncheckRecv(writer);
            return recvLength;
        }

        /// <summary>
        /// This function does not check any parameters
        /// </summary>
        int UncheckRecv(Span<byte> buffer)
        {
            var recover = rcv_queue.Count >= rcv_wnd;

            #region Merge fragment

            var recvLength = 0;

            lock (rcv_queueLock)
            {
                var count = 0;

                foreach (var seg in rcv_queue)
                {
                    seg.data.CopyTo(buffer[recvLength..]);
                    recvLength += (int)seg.len;
                    count++;
                    int frg = seg.frg;
                    SegmentManager.Free(seg);
                    if (frg == 0) break;
                }

                if (count > 0)
                {
                    rcv_queue.RemoveRange(0, count);
                }
            }

            #endregion

            Move_Rcv_buf_2_Rcv_queue();

            #region Fast recover

            lock (rcv_queueLock)
            {
                if (rcv_queue.Count < rcv_wnd && recover)
                {
                    // Ready to send back IKCP_CMD_WINS in ikcp_flush
                    // Tell remote my window size
                    probe |= IKCP_ASK_TELL;
                }
            }

            #endregion

            return recvLength;
        }

        /// <summary>
        /// This function does not check any parameters
        /// </summary>
        int UncheckRecv(IBufferWriter<byte> writer)
        {
            var recover = rcv_queue.Count >= rcv_wnd;

            #region Merge fragment

            var recvLength = 0;

            lock (rcv_queueLock)
            {
                var count = 0;

                foreach (var seg in rcv_queue)
                {
                    var len = (int)seg.len;
                    var destination = writer.GetSpan(len);
                    seg.data.CopyTo(destination);
                    writer.Advance(len);
                    recvLength += len;
                    count++;
                    int frg = seg.frg;
                    SegmentManager.Free(seg);
                    if (frg == 0) break;
                }

                if (count > 0)
                {
                    rcv_queue.RemoveRange(0, count);
                }
            }

            #endregion

            Move_Rcv_buf_2_Rcv_queue();

            #region Fast recover

            if (rcv_queue.Count < rcv_wnd && recover)
            {
                // Ready to send back IKCP_CMD_WINS in ikcp_flush
                // Tell remote my window size
                probe |= IKCP_ASK_TELL;
            }

            #endregion

            return recvLength;
        }

        /// <summary>
        /// Check the size of next message in recv queue
        /// </summary>
        /// <returns></returns>
        public int PeekSize()
        {
            lock (rcv_queueLock)
            {
                if (rcv_queue.Count == 0)
                {
                    // No package available
                    return -1;
                }

                var seq = rcv_queue[0];

                if (seq.frg == 0)
                {
                    return (int)seq.len;
                }

                if (rcv_queue.Count < seq.frg + 1)
                {
                    // Not enough packages
                    return -1;
                }

                uint length = 0;

                foreach (var seg in rcv_queue)
                {
                    length += seg.len;
                    if (seg.frg == 0) break;
                }

                return (int)length;
            }
        }
    }
}
