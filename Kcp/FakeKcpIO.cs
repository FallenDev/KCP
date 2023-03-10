using System.Buffers;
using System.Threading.Tasks;

namespace System.Net.Sockets.Kcp
{
    /// <summary>
    /// KCP IO class for debugging, no KCP function
    /// </summary>
    public class FakeKcpIO : IKcpIO
    {
        QueuePipe<byte[]> recv = new();
        public int Input(ReadOnlySpan<byte> span)
        {
            var buffer = new byte[span.Length];
            span.CopyTo(buffer);
            recv.Write(buffer);
            return 0;
        }

        public int Input(ReadOnlySequence<byte> span)
        {
            var buffer = new byte[span.Length];
            span.CopyTo(buffer);
            return Input(buffer);
        }

        public async ValueTask RecvAsync(IBufferWriter<byte> writer, object options = null)
        {
            var buffer = await recv.ReadAsync().ConfigureAwait(false);
            var target = writer.GetMemory(buffer.Length);
            buffer.AsSpan().CopyTo(target.Span);
            writer.Advance(buffer.Length);
        }

        public async ValueTask<int> RecvAsync(ArraySegment<byte> buffer, object options = null)
        {
            var temp = await recv.ReadAsync().ConfigureAwait(false);
            temp.AsSpan().CopyTo(buffer);
            return temp.Length;
        }

        QueuePipe<byte[]> send = new();
        public int Send(ReadOnlySpan<byte> span, object options = null)
        {
            var buffer = new byte[span.Length];
            span.CopyTo(buffer);
            send.Write(buffer);
            return 0;
        }

        public int Send(ReadOnlySequence<byte> span, object options = null)
        {
            var buffer = new byte[span.Length];
            span.CopyTo(buffer);
            return Send(buffer);
        }

        public async ValueTask OutputAsync(IBufferWriter<byte> writer, object options = null)
        {
            var buffer = await send.ReadAsync().ConfigureAwait(false);
            Write(writer, buffer);
        }

        private static void Write(IBufferWriter<byte> writer, byte[] buffer)
        {
            var span = writer.GetSpan(buffer.Length);
            buffer.AsSpan().CopyTo(span);
            writer.Advance(buffer.Length);
        }
    }
}
