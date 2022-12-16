using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets.Kcp.Simple;
using System.Threading.Tasks;

namespace TestClient
{
    class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Press F1 send word.......");

            var kcpClient = new SimpleKcpClient(50001, End);
            kcpClient.kcp.TraceListener = new ConsoleTraceListener();

            Task.Run(async () =>
            {
                while (true)
                {
                    kcpClient.kcp.Update(DateTimeOffset.UtcNow);
                    await Task.Delay(10);
                }
            });

            while (true)
            {
                var k = Console.ReadKey();
                if (k.Key == ConsoleKey.F1)
                {
                    Send(kcpClient, "Send a message");
                }
            }
        }

        private static readonly IPEndPoint End = new(IPAddress.Loopback, 40001);

        private static async void Send(SimpleKcpClient client, string v)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(v);
            client.SendAsync(buffer, buffer.Length);
            var resp = await client.ReceiveAsync();
            var respStr = System.Text.Encoding.UTF8.GetString(resp);
            Console.WriteLine($"Received reply from server:    {respStr}");
        }
    }
}
