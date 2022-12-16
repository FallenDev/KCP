﻿using System;
using System.Buffers;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Net.Sockets.Kcp.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void ConvertTimeTest()
        {
            var dateTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, default);
            var t1 = dateTime.ConvertTime();
            var t2 = dateTime.ConvertTime2();
            var t3 = dateTime.ConvertTimeOld();
            Assert.AreEqual(t1, t3);
            Assert.AreEqual(t2, t3);
        }

        [TestMethod]
        public void ReadHeaderTest()
        {
            unsafe
            {
                // Allocate this segment on the stack, this segment will be destroyed as it is used, and will not be saved
                const int len = KcpSegment.LocalOffset + KcpSegment.HeadOffset;
                var ptr = stackalloc byte[len];
                var seg = new KcpSegment(ptr, 0)
                {
                    conv = 1001,
                    cmd = 100,
                    frg = 20,
                    wnd = 128,
                    ts = 20,
                    sn = 9999,
                    una = 1002
                };

                Span<byte> buffer = stackalloc byte[100];
                seg.Encode(buffer);

                uint ts = 0;
                uint sn = 0;
                uint length = 0;
                uint una = 0;
                uint conv_ = 0;
                ushort wnd = 0;
                byte cmd = 0;
                byte frg = 0;

                KcpCore<KcpSegment>.ReadHeader(buffer,
                                     ref conv_,
                                     ref cmd,
                                     ref frg,
                                     ref wnd,
                                     ref ts,
                                     ref sn,
                                     ref una,
                                     ref length);
                Assert.AreEqual(seg.conv, conv_);
                Assert.AreEqual(seg.cmd, cmd);
                Assert.AreEqual(seg.frg, frg);
                Assert.AreEqual(seg.wnd, wnd);
                Assert.AreEqual(seg.ts, ts);
                Assert.AreEqual(seg.sn, sn);
                Assert.AreEqual(seg.una, una);
                Assert.AreEqual(seg.len, length);
            }
        }
    }
}

namespace UnitTestProject1
{
    public class Handle : IKcpCallback
    {
        public Action<Memory<byte>> Out;
        public Action<byte[]> Recv;

        public void Receive(byte[] buffer)
        {
            Recv(buffer);
        }

        public void Output(IMemoryOwner<byte> buffer, int aValidLength)
        {
            using (buffer)
            {
                Out(buffer.Memory[..aValidLength]);
            }
        }
    }

    [TestClass]
    public class UnitTest1
    {
        public const string Message =
        #region MyRegion

            @"LICENSE SYSTEM [2017918 10:58:53] Next license update check is after 2025-06-30T00:00:00

Built from '5.5/release' branch; Version is '5.5.0f3 (38b4efef76f0) revision 3716335'; Using compiler version '160040219'
OS: 'Windows 7 Service Pack 1 (6.1.7601) 64bit' Language: 'zh' Physical Memory: 16224 MB
BatchMode: 0, IsHumanControllingUs: 1, StartBugReporterOnCrash: 1, Is64bit: 1, IsPro: 1
Initialize mono
Mono path[0] = 'C:/Program Files/Unity5.5.0/Editor/Data/Managed'
Mono path[1] = 'C:/Program Files/Unity5.5.0/Editor/Data/Mono/lib/mono/2.0'
Mono path[2] = 'C:/Program Files/Unity5.5.0/Editor/Data/UnityScript'
Mono config path = 'C:/Program Files/Unity5.5.0/Editor/Data/Mono/etc'
Using monoOptions --debugger-agent=transport=dt_socket,embedding=1,defer=y,address=0.0.0.0:56392
IsTimeToCheckForNewEditor: Update time 1505705705 current 1505703540
C:/work/irobotqv2.0_dev/irobotqv2.0_app
Loading GUID <-> Path mappings...0.000281 seconds
Loading Asset Database...0.015599 seconds
Audio: FMOD Profiler initialized on port 54900
AssetDatabase consistency checks...0.019115 seconds
Initialize engine version: 5.5.0f3 (38b4efef76f0)
GfxDevice: creating device client; threaded=1
Direct3D:
    Version:  Direct3D 11.0 [level 11.0]
    Renderer: AMD Radeon HD 6670 (ID=0x6758)
    Vendor:   ATI
    VRAM:     4418 MB
    Driver:   14.100.0.0
Begin MonoManager ReloadAssembly1
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly2
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly3
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly4
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)
Begin MonoManager ReloadAssembly5
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEngine.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\UnityEditor.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.Locator.dll (this message is harmless)
Refreshing native plugins compatible for Editor in 9.17 ms, found 3 plugins.
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\I18N.CJK.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.DataContract.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Core.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Managed\Unity.IvyParser.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Xml.dll (this message is harmless)
Platform assembly: C:\Program Files\Unity5.5.0\Editor\Data\Mono\lib\mono\2.0\System.Configuration.dll (this message is harmless)";

        #endregion

        [TestMethod]
        public void TestKcp()
        {
            const int echoTimes = 3;
            const int conv = 123;
            var random = new Random();
            var handle1 = new Handle();
            var handle2 = new Handle();
            var kcp1 = new SimpleSegManager.Kcp(conv, handle1);
            var kcp2 = new SimpleSegManager.Kcp(conv, handle2);
            kcp1.NoDelay(1, 10, 2, 1); // fast
            kcp2.NoDelay(1, 10, 2, 1); // fast

            var sendByte = Encoding.ASCII.GetBytes(Message);

            handle1.Out += buffer =>
            {
                var next = random.Next(100);
                
                // random packet loss
                if (next >= 5) kcp2.Input(buffer.Span);
            };

            handle2.Out += buffer => kcp1.Input(buffer.Span);

            var end = 0;

            handle1.Recv += buffer =>
            {
                var str = Encoding.ASCII.GetString(buffer);
                Assert.AreEqual(Message, str);
                Interlocked.Increment(ref end);

                if (end < echoTimes)
                {
                    kcp1.Send(buffer);
                }
            };

            handle2.Recv += buffer => kcp2.Send(buffer);

            Task.Run(async () =>
            {
                try
                {
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
                    }
                }
                catch (Exception e)
                {
                    e.ToString();
                }

            });

            Task.Run(async () =>
            {
                try
                {
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
                    }
                }
                catch (Exception e)
                {
                    e.ToString();
                }
            });

            kcp1.Send(sendByte);

            var task = Task.Run(async () =>
            {
                while (end < echoTimes)
                {
                    await Task.Yield();
                }
                return 1;
            });

            task.Result.ToString();
        }

        [TestMethod]
        public void TestKcpSegmentFree()
        {
            KcpSegment.FreeHGlobal(default);
        }
    }
}
