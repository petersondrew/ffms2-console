using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using ffms2.console.ipc;
using FFMSSharp;
using Zyan.Communication;
using Zyan.Communication.Protocols.Ipc;

namespace ffms2.console
{
    internal static class Program
    {
        static void OnProgress(object sender, IndexingProgressChangeEventArgs eventArgs)
        {
            var cursorTop = Console.CursorTop;
            Console.SetCursorPosition(0, cursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, cursorTop);
            Console.Write("{0:P1}", (double)eventArgs.Current / eventArgs.Total);
        }

        static void Test()
        {
            // Index the source file.
            const string file = @"E:\Code\ForensicVideoSolutions\Test Files\requires force h264.ave";

            // ReSharper disable once AssignNullToNotNullAttribute
            var indexFile = Path.Combine(Path.GetDirectoryName(file), $"{Path.GetFileName(file)}.idx");
            var indexExists = File.Exists(indexFile);

            FFMS2.Initialize();
            if (!FFMS2.Initialized)
                throw new Exception("Unable to initialize FFMS2");

            using (var indexer = new Indexer(file))
            {
                if (indexExists)
                    Console.WriteLine("Loading index from {0}", indexFile);
                else
                {
                    Console.WriteLine("Indexing file {0}...", indexFile);
                    indexer.UpdateIndexProgress += OnProgress;
                }

                using (var index = indexExists ? new Index(indexFile) : indexer.Index())
                {
                    // Save index if necessary
                    if (!indexExists)
                    {
                        Console.WriteLine("Saving index to file {0}...", indexFile);
                        index.WriteIndex(indexFile);
                        indexer.UpdateIndexProgress -= OnProgress;
                    }

                    Console.WriteLine();

                    // Retrieve the track number of the first video track
                    var firstVideoTrackNumber = index.GetFirstTrackOfType(TrackType.Video);
                    var videoTracks = new List<int>();
                    for (var i = 0; i < index.NumberOfTracks; i++)
                    {
                        if (index.GetTrack(i).TrackType == TrackType.Video)
                            videoTracks.Add(i);
                    }
                    Console.WriteLine("Video contains {0} video tracks", videoTracks.Count);

                    foreach (var track in videoTracks)
                    {
                        var source = index.VideoSource(file, track, 0);
                        Console.WriteLine("Video track {0} has {1} frames and begins at timestamp {2:c} and ends at {3:c}",
                                          track,
                                          source.NumberOfFrames,
                                          TimeSpan.FromSeconds(source.FirstTime),
                                          TimeSpan.FromSeconds(source.LastTime));
                    }

                    // We now have enough information to create the video source object
                    var videoSource = index.VideoSource(file, firstVideoTrackNumber);

                    // Get the first frame for examination so we know what we're getting.
                    // This is required because resolution and colorspace is a per frame property and NOT global for the video.
                    //var frameSource = videoSource.GetFrame(0);
                    //// Note: Slightly convoluted, just for testing our dictionary...
                    //var frameInfo = videoSource.Track.GetFrameInfo(0);
                    //Console.WriteLine(@"Getting first frame of first video track at timestamp {1:c}",
                    //                  TimeSpan.FromMilliseconds(frameInfo.PTS * videoSource.Track.TimeBaseNumerator / (double) videoSource.Track.TimeBaseDenominator));

                    //TODO: I'm not crazy about the int/long/double overload thing
                    //var frameSource = videoSource.GetFrameByPosition(3761762);
                    var frameSource = videoSource.GetFrame(0);

                    /* Now you may want to do something with the info; particularly interesting values are:
                     * frameSource.EncodedResolution.Width; (frame width in pixels)
                     * frameSource.EncodedResolution.Height; (frame height in pixels)
                     * frameSource.EncodedPixelFormat; (actual frame colorspace)
                     */

                    /* If you want to change the output colorspace or resize the output frame size, now is the time to do it.
                     * IMPORTANT: This step is also required to prevent resolution and colorspace changes midstream.
                     * You can you can always tell a frame's original properties by examining the Encoded* properties in FFMSSharp.Frame.
                     * See libavutil/pixfmt.h for the list of pixel formats/colorspaces.
                     * To get the name of a given pixel format, strip the leading PIX_FMT_ and convert to lowercase.
                     * For example, PIX_FMT_YUV420P becomes "yuv420p".
                     */

                    // A list of the acceptable output formats
                    var pixelFormats = new List<int> { FFMS2.GetPixelFormat("bgra") };

                    videoSource.SetOutputFormat(pixelFormats, frameSource.EncodedResolution.Width, frameSource.EncodedResolution.Height, Resizer.Bicubic);

                    // Now we're ready to actually retrieve the video frames.
                    var frameNumber = 0;
                    var frame = videoSource.GetFrame(frameNumber); // Valid until next call to GetFrame on the same video object
                    
                    using (var image = File.Create("frame.bmp"))
                        frame.Bitmap.Save(image, ImageFormat.Bmp);

                    Process.Start("frame.bmp");
                }
            }
        }

        static readonly ManualResetEventSlim QuitEvent = new ManualResetEventSlim(false);

        static int Main(string[] args)
        {
            string portName;
#if DEBUG
            portName = "localhost";
#else
            if (args.Length < 1 || String.IsNullOrEmpty(args[0]))
            {
                Console.Error.WriteLine("Please specify a unique IPC port name");
                return 1;
            }
            portName = args[0];
#endif

            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            try
            {
                using (var host = new ZyanComponentHost("ffms2.console", new IpcBinaryServerProtocolSetup(portName)))
                {
                    host.RegisterComponent<IFrameRetrievalService, FrameRetrievalService>(ActivationType.Singleton);
                    Console.WriteLine("Started new host on {0}", host.DiscoverableUrl);
                    Console.WriteLine("Press Ctrl-C to quit");
                    QuitEvent.Wait();
                }
                //Test();
                QuitEvent.Wait();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= ConsoleOnCancelKeyPress;
            }

            return 0;
        }

        static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs consoleCancelEventArgs)
        {
            consoleCancelEventArgs.Cancel = true;
            QuitEvent.Set();
        }
    }
}
