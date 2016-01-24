using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ffms2.console.ipc;
using Zyan.Communication;
using Zyan.Communication.Protocols.Ipc;

namespace TestClient
{
    internal static class Program
    {
        static readonly object ConsoleLock = new object();

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
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
                //if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave", useCached: true))
                //if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave", useCached: true, videoCodec: "h264"))
                //if (!proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\Back Entrance-12-17-14-330-600.avi", useCached: true, alternateIndexCacheFileLocation: @"E:\code\foo.idx"))
                //if (!proxy.Index(@"E:\Videos\rgb_test.avi", useCached: false))
                if (!proxy.Index(@"E:\Videos\yuv411p.avi", useCached: false))
                {
                    Console.Error.WriteLine($"Error indexing file:{Environment.NewLine}{proxy.LastException}{Environment.NewLine}Press any key to quit");
                    Console.ReadKey();
                    return;
                }
                ProxyOnIndexProgress(new IndexProgressEventArgs(1, 1));
                Console.WriteLine();
                Console.WriteLine();

                var handle = IntPtr.Zero;

                // Window ready signal
                var windowReady = new ManualResetEventSlim(false);

                // Set up remaining program logic and window event loop
                var indexingTask = Task.Factory.StartNew(async () =>
                {
                    if (!windowReady.Wait(TimeSpan.FromSeconds(60)))
                        return;

                    proxy.SetFrameOutputFormat(pixelFormat: FramePixelFormat.YV12);

                    for (var frameNumber = 0; frameNumber < 10; frameNumber++)
                    {
                        var frame = proxy.GetFrame(0, frameNumber);
                        Console.WriteLine($"Frame number: {frame.FrameNumber}");
                        Console.WriteLine($"Frame type: {frame.FrameType}");
                        Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                        Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                        Console.WriteLine($"Frame position: {frame.FilePos}");
                        Console.WriteLine($"Frame resolution: {frame.Resolution}");
                        await Task.Delay(100).ConfigureAwait(false);
                        // Ask to display image
                        proxy.DisplayFrame(frame, handle);
                    }
                });

                var formTask = Task.Factory.StartNew(() =>
                {
                    // Set up display window
                    var preview = new PreviewForm();
                    preview.Show();
                    handle = preview.Handle;
                    preview.Activate();

                    windowReady.Set();
                    Application.Run(preview);
                });

                Task.WaitAll(formTask, indexingTask);

                //foreach (var frame in proxy.GetFrameInfos(0))
                //{
                //    Console.WriteLine($"Frame number: {frame.FrameNumber}");
                //    Console.WriteLine($"Frame type: {frame.FrameType}");
                //    Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                //    Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                //    Console.WriteLine($"Frame position: {frame.FilePos}");
                //    Console.WriteLine($"Frame resolution: {frame.Resolution}");
                //}

                //for (var frameNumber = 0; frameNumber < 100; frameNumber++)
                //{
                //    var frame = proxy.GetFrame(0, frameNumber);
                //    Console.WriteLine($"Frame number: {frame.FrameNumber}");
                //    Console.WriteLine($"Frame type: {frame.FrameType}");
                //    Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                //    Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                //    Console.WriteLine($"Frame position: {frame.FilePos}");
                //    Console.WriteLine($"Frame resolution: {frame.Resolution}");
                //    var bitmap = frame.ExtractBitmap();
                //    bitmap.Dispose();
                //}

                //var frame = proxy.GetFrame(0, 0);
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