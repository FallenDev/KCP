﻿using System.Threading.Tasks;
using System.Threading;
using BufferOwner = System.Buffers.IMemoryOwner<byte>;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// Kcp回调
    /// </summary>
    public interface IKcpCallback
    {
        /// <summary>
        /// kcp 发送方向输出
        /// </summary>
        /// <param name="buffer">kcp 交出发送缓冲区控制权，缓冲区来自<see cref="RentBuffer(int)"/></param>
        /// <param name="avalidLength">数据的有效长度</param>
        /// <returns>不需要返回值</returns>
        /// <remarks>通过增加 avalidLength 能够在协议栈中有效的减少数据拷贝</remarks>
        void Output(BufferOwner buffer, int avalidLength);
    }

    /// <summary>
    /// Kcp回调
    /// </summary>
    public interface IKcpOutputer
    {
        Span<byte> GetSpan(int sizeHint = 0);
        void Advance(int bytes);
        void Flush();
    }


    /// <summary>
    /// 外部提供缓冲区,可以在外部链接一个内存池
    /// </summary>
    public interface IRentable
    {
        /// <summary>
        /// 外部提供缓冲区,可以在外部链接一个内存池
        /// </summary>
        BufferOwner RentBuffer(int length);
    }

    public interface IKcpSetting
    {
        int Interval(int interval);
        /// <summary>
        /// fastest: ikcp_nodelay(kcp, 1, 20, 2, 1)
        /// </summary>
        /// <param name="nodelay">0:disable(default), 1:enable</param>
        /// <param name="interval">internal update timer interval in millisec, default is 100ms</param>
        /// <param name="resend">0:disable fast resend(default), 1:enable fast resend</param>
        /// <param name="nc">0:normal congestion control(default), 1:disable congestion control</param>
        /// <returns></returns>
        int NoDelay(int nodelay, int interval, int resend, int nc);
        /// <summary>
        /// change MTU size, default is 1400
        /// <para>** 这个方法不是线程安全的。请在没有发送和接收时调用 。</para>
        /// </summary>
        /// <param name="mtu"></param>
        /// <returns></returns>
        /// <remarks>
        /// 如果没有必要，不要修改Mtu。过小的Mtu会导致分片数大于接收窗口，造成kcp阻塞冻结。
        /// </remarks>
        int SetMtu(int mtu = 1400);
        /// <summary>
        /// set maximum window size: sndwnd=32, rcvwnd=128 by default
        /// </summary>
        /// <param name="sndwnd"></param>
        /// <param name="rcvwnd"></param>
        /// <returns></returns>
        /// <remarks>
        /// 如果没有必要请不要修改。注意确保接收窗口必须大于最大分片数。
        /// </remarks>
        int WndSize(int sndwnd = 32, int rcvwnd = 128);
    }

    public interface IKcpUpdate
    {
        [Obsolete("Use Update(in DateTimeOffset time) instaed.")]
        void Update(in DateTime time);
        void Update(in DateTimeOffset time);
    }
}




