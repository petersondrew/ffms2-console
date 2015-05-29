using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using ffms2.console.ipc;
using Zyan.Communication;
using Zyan.Communication.Protocols.Ipc;

namespace TestClient
{
    internal static class Program
    {
        static void Main()
        {
#if DEBUG
            var pipeName = "localhost";
#else
            var pipeName = Guid.NewGuid().ToString("N");
            var server = Process.Start(@"E:\Code\ffms2-console\src\ffms2-console\bin\x86\Debug\ffms2.console.exe", pipeName);
#endif
            Thread.Sleep(TimeSpan.FromSeconds(2));
            var protocol = new IpcBinaryClientProtocolSetup();
            var url = protocol.FormatUrl(pipeName, "ffms2.console");

            using (var connection = new ZyanConnection(url, protocol))
            {
                Console.WriteLine("Connected to server");
                var proxy = connection.CreateProxy<IFrameRetrievalService>();

                proxy.IndexProgress += ProxyOnIndexProgress;

                Console.WriteLine("Indexing file");
                if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\Back Entrance-12-17-14-330-600.avi", false))
                {
                    Console.Error.WriteLine("Error indexing file, press any key to quit");
                    Console.ReadKey();
                    return;
                }
                var frame = proxy.GetFrame(0, 0);
                Console.WriteLine("Frame number: {0}", frame.FrameNumber);
                Console.WriteLine("Frame type: {0}", frame.FrameType);
                Console.WriteLine("Frame PTS: {0}", TimeSpan.FromMilliseconds(frame.PTS));
                using (var image = File.Create("frame.bmp"))
                    frame.ExtractBitmap().Save(image, ImageFormat.Bmp);

                Process.Start("frame.bmp");
                proxy.IndexProgress -= ProxyOnIndexProgress;
            }
#if !DEBUG
            if (server != null && !server.HasExited) server.Close();
#endif
        }

        static void ProxyOnIndexProgress(object sender, IndexProgressEventArgs indexProgressEventArgs)
        {
            var cursorTop = Console.CursorTop;
            Console.SetCursorPosition(0, cursorTop);
            Console.Write(new String(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, cursorTop);
            Console.Write("{0:P1}", indexProgressEventArgs.Progress);
        }
    }
}