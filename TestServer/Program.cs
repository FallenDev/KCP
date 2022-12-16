using System;
using System.Diagnostics;
using System.Net.Sockets.Kcp.Simple;
using System.Threading.Tasks;

namespace TestServer
{
    class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var kcpClient = new SimpleKcpClient(40001);
            kcpClient.kcp.TraceListener = new ConsoleTraceListener();

            Task.Run(async () =>
            {
                while (true)
                {
                    kcpClient.kcp.Update(DateTimeOffset.UtcNow);
                    await Task.Delay(10);
                }
            });

            StartRecv(kcpClient);
            Console.ReadLine();
        }

        static async void StartRecv(SimpleKcpClient client)
        {
            while (true)
            {
                var res = await client.ReceiveAsync();
                var str = System.Text.Encoding.UTF8.GetString(res);
                if ("Send a message" != str) continue;
                Console.WriteLine(str);
                var buffer = System.Text.Encoding.UTF8.GetBytes("Reply to a message");
                client.SendAsync(buffer, buffer.Length);
            }
        }
    }
}
