using System.Threading.Tasks;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;
using System.Buffers;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// KCP Call back
    /// </summary>
    public interface IKcpCallback
    {
        /// <summary>
        /// Send direction output
        /// </summary>
        /// <param name="buffer">Hand over control of the send buffer, buffer from <see cref="IRentable.RentBuffer(int)"/></param>
        /// <param name="avalidLength">Effective length of data</param>
        /// <remarks>By increasing aValidLength, data copying can be effectively reduced in the protocol stack</remarks>
        void Output(BufferOwner buffer, int avalidLength);
    }

    /// <summary>
    /// KCP Call back
    /// </summary>
    /// <remarks>
    /// Failed design <see cref="KcpOutputWriter.Output(BufferOwner, int)"/>. IMemoryOwner there is no way to replace,
    /// here is only equivalent to IKcpCallback and IRentable
    /// </remarks>
    public interface IKcpOutputWriter : IBufferWriter<byte>
    {
        int UnflushedBytes { get; }
        void Flush();
    }

    /// <summary>
    /// Provide buffers externally, you can link a memory pool externally
    /// </summary>
    public interface IRentable
    {
        /// <summary>
        /// Provide buffers externally, you can link a memory pool externally
        /// </summary>
        BufferOwner RentBuffer(int length);
    }

    public interface IKcpSetting
    {
        int Interval(int interval);

        /// <summary>
        /// Fastest: ikcp_nodelay(kcp, 1, 20, 2, 1)
        /// </summary>
        /// <param name="nodelay">0:disable(default), 1:enable</param>
        /// <param name="interval">internal update timer interval in millisecond, default is 100ms</param>
        /// <param name="resend">0:disable fast resend(default), 1:enable fast resend</param>
        /// <param name="nc">0:normal congestion control(default), 1:disable congestion control</param>
        int NoDelay(int nodelay, int interval, int resend, int nc);

        /// <summary>
        /// Change MTU size, default is 1400
        /// <para>** This method is not thread-safe. Call it when there is no sending or receiving</para>
        /// </summary>
        /// <remarks>
        /// Do not modify Mtu if not necessary. Too small MTU will cause the number of fragments to be larger than the receiving window, causing KCP blocking, freezing
        /// </remarks>
        int SetMtu(int mtu = 1400);

        /// <summary>
        /// Set maximum window size: sndwnd=32, rcvwnd=128 by default
        /// </summary>
        /// <remarks>
        /// Do not modify if not necessary. Note: The receiving window must be larger than the maximum number of fragments
        /// </remarks>
        int WndSize(int sndwnd = 32, int rcvwnd = 128);
    }

    public interface IKcpUpdate
    {
        void Update(in DateTimeOffset time);
    }

    public interface IKcpSendable
    {
        /// <summary>
        /// Send the data to be sent to the network to the KCP protocol
        /// </summary>
        int Send(ReadOnlySpan<byte> span, object options = null);

        /// <summary>
        /// Send the data to be sent to the network to the KCP protocol
        /// </summary>
        int Send(ReadOnlySequence<byte> span, object options = null);
    }

    public interface IKcpInputable
    {
        /// <summary>
        /// After the lower layer receives the data, it is added to the KCP protocol
        /// </summary>
        int Input(ReadOnlySpan<byte> span);

        /// <summary>
        /// After the lower layer receives the data, it is added to the KCP protocol
        /// </summary>
        int Input(ReadOnlySequence<byte> span);
    }

    /// <summary>
    /// KCP protocol input and output standard interface
    /// </summary>
    public interface IKcpIO : IKcpSendable, IKcpInputable
    {
        /// <summary>
        /// Take an integrated packet from KCP
        /// </summary>
        ValueTask RecvAsync(IBufferWriter<byte> writer, object options = null);

        /// <summary>
        /// Take an integrated packet from KCP
        /// </summary>
        /// <returns>Data length</returns>
        ValueTask<int> RecvAsync(ArraySegment<byte> buffer, object options = null);

        /// <summary>
        /// Take out the data that needs to be sent to the network from the KCP protocol.
        /// </summary>
        ValueTask OutputAsync(IBufferWriter<byte> writer, object options = null);
    }

}




