using System;
using System.Diagnostics;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnitTestProject1;

namespace TestKCP
{
    class Program
    {
        private static string ShowThread => $"  ThreadID[{Thread.CurrentThread.ManagedThreadId}]";

        private class TL : ConsoleTraceListener
        {
            public override void WriteLine(string message, string category)
            {
                base.WriteLine(message, $"[{Name}]  {category}");
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine(ShowThread);
            var random = new Random();
            var handle1 = new Handle();
            var handle2 = new Handle();
            const int conv = 123;
            var kcp1 = new PoolSegManager.Kcp(conv, handle1);
            var kcp2 = new PoolSegManager.Kcp(conv, handle2);
            kcp1.TraceListener = new TL() { Name = "Kcp1" };
            kcp2.TraceListener = new TL() { Name = "Kcp2" };
            kcp1.NoDelay(1, 10, 2, 1); // fast
            kcp1.WndSize(128);
            kcp2.NoDelay(1, 10, 2, 1); // fast
            kcp2.WndSize(128);
            var sendByte = Encoding.ASCII.GetBytes(UnitTest1.Message);

            handle1.Out += buffer =>
            {
                var next = random.Next(100);

                // Random packet loss
                if (next >= 15)
                {
                    Task.Run(() =>
                    {
                        kcp2.Input(buffer.Span);
                    });
                }
            };

            handle2.Out += buffer =>
            {
                var next = random.Next(100);

                // Random packet loss
                if (next >= 0)
                {
                    Task.Run(() =>
                    {
                        kcp1.Input(buffer.Span);
                    });
                }
                else
                {
                    Console.WriteLine("Response missed");
                }
            };

            var count = 0;
            handle1.Recv += buffer =>
            {
                var str = Encoding.ASCII.GetString(buffer);
                count++;
                if (UnitTest1.Message == str) Console.WriteLine($"KCP Echo----{count}");

                var res = kcp1.Send(buffer);
                if (res != 0) Console.WriteLine("KCP Send error");
            };

            var recvCount = 0;
            handle2.Recv += buffer =>
            {
                recvCount++;
                Console.WriteLine($"KCP2 Received----{recvCount}");
                var res = kcp2.Send(buffer);
                if (res != 0) Console.WriteLine("KCP Send error");
            };

            Task.Run(async () =>
            {
                try
                {
                    var updateCount = 0;
                    while (true)
                    {
                        kcp1.Update(DateTimeOffset.UtcNow);
                        int len;

                        while ((len = kcp1.PeekSize()) > 0)
                        {
                            var buffer = new byte[len];
                            if (kcp1.Recv(buffer) >= 0)
                            {
                                handle1.Receive(buffer);
                            }
                        }

                        await Task.Delay(5);
                        updateCount++;
                        if (updateCount % 1000 == 0) Console.WriteLine($"KCP1 Alive {updateCount}----{ShowThread}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

            Task.Run(async () =>
            {
                try
                {
                    var updateCount = 0;

                    while (true)
                    {
                        kcp2.Update(DateTimeOffset.UtcNow);
                        int len;

                        do
                        {
                            var (buffer, aValidSize) = kcp2.TryRecv();
                            len = aValidSize;
                            if (buffer == null) continue;
                            var temp = new byte[len];
                            buffer.Memory.Span[..len].CopyTo(temp);
                            handle2.Receive(temp);
                        } while (len > 0);

                        await Task.Delay(5);
                        updateCount++;

                        if (updateCount % 1000 == 0) Console.WriteLine($"KCP2 Alive {updateCount}----{ShowThread}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

            kcp1.Send(sendByte);

            while (true)
            {
                Thread.Sleep(1000);
                GC.Collect();
            }
        }
    }
}
