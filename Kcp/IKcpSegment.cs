namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// KCP Header
    /// https://zhuanlan.zhihu.com/p/559191428
    /// </summary>
    public interface IKcpHeader
    {
        /// <summary>
        /// Session number, the two parties will communicate only when they match
        /// </summary>
        uint conv { get; set; }

        /// <summary>
        /// Command Type
        /// </summary>
        /// <remarks>
        /// <para/> IKCP_CMD_PUSH = 81   // cmd: push data datagram
        /// <para/> IKCP_CMD_ACK  = 82   // cmd: ack confirmation message
        /// <para/> IKCP_CMD_WASK = 83   // cmd: window probe (ask) Window detection message, inquiring about the size of the remaining receiving window at the peer end.
        /// <para/> IKCP_CMD_WINS = 84   // cmd: window size (tell) Window notification message, informing the peer of the size of the remaining receiving window.
        /// </remarks>
        byte cmd { get; set; }

        /// <summary>
        /// The number of remaining fragments indicates how many subsequent packets belong to the same packet.
        /// </summary>
        byte frg { get; set; }

        /// <summary>
        /// Available window size
        /// </summary>
        ushort wnd { get; set; }

        /// <summary>
        /// Timestamp sent <seealso cref="DateTimeOffset.ToUnixTimeMilliseconds"/>
        /// </summary>
        uint ts { get; set; }

        /// <summary>
        /// Acknowledgment number or message number
        /// </summary>
        uint sn { get; set; }

        /// <summary>
        /// Assigned before the serial number have been received
        /// </summary>
        uint una { get; set; }

        /// <summary>
        /// Data length
        /// </summary>
        uint len { get; }
    }

    public interface IKcpSegment : IKcpHeader
    {
        /// <summary>
        /// Timestamp of the retransmission. Resend this packet past the current time
        /// </summary>
        uint resendts { get; set; }

        /// <summary>
        /// Overtime retransmission time, according to the network to determine
        /// </summary>
        uint rto { get; set; }

        /// <summary>
        /// Fast retransmission mechanism, record the number of skipped times, and perform fast retransmission if the number is exceeded
        /// </summary>
        uint fastack { get; set; }

        /// <summary>
        /// Number of retransmissions
        /// </summary>
        uint xmit { get; set; }

        /// <summary>
        /// Data content
        /// </summary>
        Span<byte> data { get; }

        /// <summary>
        /// Encode IKcpSegment into byte array and return total length (including Kcp header)
        /// </summary>
        int Encode(Span<byte> buffer);
    }

    public interface ISegmentManager<Segment> where Segment : IKcpSegment
    {
        Segment Alloc(int appendDateSize);
        void Free(Segment seg);
    }
}
