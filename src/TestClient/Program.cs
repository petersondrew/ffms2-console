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
        static readonly object ConsoleLock = new object();

        static void Main()
        {
#if DEBUG
            const string pipeName = "localhost";
#else
            //var pipeName = Guid.NewGuid().ToString("N");
            var pipeName = "localhost";
            var server = Process.Start(@"E:\Code\ffms2-console\src\ffms2-console\bin\x86\Release\ffms2.console.exe", pipeName);
#endif
            Thread.Sleep(TimeSpan.FromSeconds(2));
            var protocol = new IpcBinaryClientProtocolSetup();
            var url = protocol.FormatUrl(pipeName, "ffms2.console");

            using (var connection = new ZyanConnection(url, protocol))
            {
                Console.WriteLine("Connected to server");
                var proxy = connection.CreateProxy<IFrameRetrievalService>();

                // Need to debounce this on the other end of the pipe to avoid flooding it
                proxy.IndexProgress += ProxyOnIndexProgress;

                Console.WriteLine("Indexing file");
                //if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave", useCached: false, videoCodec: "h264"))
                if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\Back Entrance-12-17-14-330-600.avi", useCached: true))
                {
                    Console.Error.WriteLine($"Error indexing file:{Environment.NewLine}{proxy.LastException}{Environment.NewLine}Press any key to quit");
                    Console.ReadKey();
                    return;
                }
                ProxyOnIndexProgress(new IndexProgressEventArgs(1, 1));
                Console.WriteLine();
                Console.WriteLine();

                //foreach (var frame in proxy.GetFrameInfos(0))
                //{
                //    Console.WriteLine($"Frame number: {frame.FrameNumber}");
                //    Console.WriteLine($"Frame type: {frame.FrameType}");
                //    Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                //    Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                //    Console.WriteLine($"Frame position: {frame.FilePos}");
                //    Console.WriteLine($"Frame resolution: {frame.Resolution}");
                //}

                for (var frameNumber = 0; frameNumber < 100; frameNumber++)
                {
                    // Unwrap to prevent marshaling ExtractBitmap call across boundaries (TODO: Can we disconnect the object so we don't have to copy?)
                    var frame = proxy.GetFrame(0, frameNumber).Unwrap();
                    Console.WriteLine($"Frame number: {frame.FrameNumber}");
                    Console.WriteLine($"Frame type: {frame.FrameType}");
                    Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                    Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                    Console.WriteLine($"Frame position: {frame.FilePos}");
                    Console.WriteLine($"Frame resolution: {frame.Resolution}");
                    var bitmap = frame.ExtractBitmap();
                    bitmap.Dispose();
                }

                // Unwrap to prevent marshaling ExtractBitmap call across boundaries (TODO: Can we disconnect the object so we don't have to copy?)
                //var frame = proxy.GetFrame(0, 0).Unwrap();
                //Console.WriteLine($"Frame number: {frame.FrameNumber}");
                //Console.WriteLine($"Frame type: {frame.FrameType}");
                //Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                //Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                //Console.WriteLine($"Frame position: {frame.FilePos}");
                //Console.WriteLine($"Frame resolution: {frame.Resolution}");
                //const string filename = "frame.bmp";
                //if (File.Exists(filename)) File.Delete(filename);
                //frame.ExtractBitmap().Save(filename, ImageFormat.Bmp);
                //Process.Start("frame.bmp");

                proxy.IndexProgress -= ProxyOnIndexProgress;
            }
#if !DEBUG
            if (server != null && !server.HasExited) server.Close();
#endif
        }

        static void ProxyOnIndexProgress(IndexProgressEventArgs indexProgressEventArgs)
        {
            lock (ConsoleLock)
            {
                var cursorTop = Console.CursorTop;
                Console.SetCursorPosition(0, cursorTop);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, cursorTop);
                Console.Write($"{indexProgressEventArgs.Progress:P1}");
            }
        }
    }
}