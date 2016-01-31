using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ffms2.console.ipc;
using ffms2.console.ipc.Exceptions;
using Zyan.Communication;
using Zyan.Communication.Protocols.Ipc;

namespace TestClient
{
    internal static class Program
    {
        static readonly object ConsoleLock = new object();

        static bool CheckForRecoverableFFMSException(Exception exception)
        {
            if (!(exception.InnerException is FFMSException))
                return false;
            var ffmsException = exception.InnerException;
            return ffmsException is FrameDisplayException || ffmsException is FrameRetrievalException ||
                   ffmsException is IndexAccessException;
        }

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
            Process server = null;
            if (!Debugger.IsAttached)
                server = Process.Start(@"E:\Code\ffms2-console\src\ffms2-console\bin\x86\Release\ffms2.console.exe", pipeName);
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
                try
                {
                    //proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave", useCached: true);
                    //proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave", useCached: true, videoCodec: "h264");
                    proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\Back Entrance-12-17-14-330-600.avi", useCached: false, alternateIndexCacheFileLocation: @"E:\code\foo.idx");
                    //proxy.Index(@"E:\Videos\rgb_test.avi", useCached: false);
                    //proxy.Index(@"E:\Videos\yuv411p.avi", useCached: false);
                    //proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\10-3-14 for DREW\For_spectrum_view.avi",
                    //    useCached: false);
                    //proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\ffms problem files\crashes.irf",
                    //    useCached: false);
                    //proxy.Index(@"E:\Code\ForensicVideoSolutions\Test Files\ffms problem files\doesnt index.MTS", useCached: false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error indexing file:{Environment.NewLine}{ex}{Environment.NewLine}Press any key to quit");
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
                var indexingTask = Task.Factory.StartNew(() =>
                {
                    if (!windowReady.Wait(TimeSpan.FromSeconds(60)))
                        return;

                    try
                    {
                        proxy.SetSeekHandling(SeekHandling.Unsafe);
                        proxy.SetFrameOutputFormat(pixelFormat: FramePixelFormat.YV12);

                        var tracks = proxy.GetTracks();

                        if (tracks.Count == 0)
                            throw new ApplicationException("No tracks found in file");

                        var videoTrack = tracks.FirstOrDefault(t => t.Type == TrackType.Video);
                        if (videoTrack == null)
                            throw new ApplicationException("No video track in file");

                        var frames = proxy.GetFrames(videoTrack.TrackNumber);

                        var timer = new Stopwatch();
                        timer.Start();

                        for (var frameNumber = 0; frameNumber <= 1000; frameNumber++)
                        {
                            try
                            {
                                //var frame = proxy.GetFrame(0, frameNumber);
                                var frame = frames[frameNumber];
                                Console.WriteLine($"Frame number: {frame.FrameNumber}");
                                Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                                Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                                Console.WriteLine($"Frame position: {frame.FilePos}");
                                //await Task.Delay(100).ConfigureAwait(false);
                                // Ask to display image
                                frame = proxy.DisplayFrame(frame, handle);
                                Console.WriteLine($"Frame type: {frame?.FrameType}");
                                Console.WriteLine($"Frame resolution: {frame?.Resolution}");
                            }
                            catch (Exception remoteException)
                            {
                                if (!CheckForRecoverableFFMSException(remoteException))
                                    throw;
                            }
                        }

                        var forwards = timer.Elapsed;
                        timer.Restart();

                        for (var frameNumber = 1000; frameNumber >= 0; frameNumber--)
                        {
                            try
                            {
                                //var frame = proxy.GetFrame(0, frameNumber);
                                var frame = frames[frameNumber];
                                Console.WriteLine($"Frame number: {frame.FrameNumber}");
                                Console.WriteLine($"Frame keyframe: {frame.KeyFrame}");
                                Console.WriteLine($"Frame PTS: {TimeSpan.FromMilliseconds(frame.PTS)}");
                                Console.WriteLine($"Frame position: {frame.FilePos}");
                                //await Task.Delay(100).ConfigureAwait(false);
                                // Ask to display image
                                frame = proxy.DisplayFrame(frame, handle);
                                Console.WriteLine($"Frame type: {frame?.FrameType}");
                                Console.WriteLine($"Frame resolution: {frame?.Resolution}");
                            }
                            catch (Exception remoteException)
                            {
                                if (!CheckForRecoverableFFMSException(remoteException))
                                    throw;
                            }
                        }

                        var backwards = timer.Elapsed;

                        Console.WriteLine($"Forwards: {forwards}; Backwards: {backwards}; Delta: {backwards - forwards}");
                    }
                    catch (Exception ex)
                    {
                        var ffmsException = ex.InnerException as FFMSException;
                        Console.Error.WriteLine(ffmsException == null
                            ? $"An error ocurred:{Environment.NewLine}{ex}"
                            : $"An error ocurred in ffms-console:{Environment.NewLine}{ffmsException}");
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

                //foreach (var frame in proxy.GetFrames(0))
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