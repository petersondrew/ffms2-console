using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using ffms2.console.ipc;
using FFMSSharp;
using sdldisplay;
using Frame = ffms2.console.ipc.Frame;

namespace ffms2.console
{
    internal sealed class FrameRetrievalService : IFrameRetrievalService, IDisposable
    {
        readonly ConcurrentDictionary<int, VideoSource> videoSources = new ConcurrentDictionary<int, VideoSource>();

        bool disposed;

        string indexedFile;

        Index index;

        int frameWidth, frameHeight;

        int displayPixelFormat;

        Resizer frameResizer;

        Display display;

        SeekMode seekMode;

        public Exception LastException { get; private set; }

        public bool Indexed { get; private set; }

        public bool Index(string file, bool useCached = true, string alternateIndexCacheFileLocation = null,
            string videoCodec = null)
        {
            Indexed = false;
            index = null;
            indexedFile = null;

            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            if (!File.Exists(file))
                throw new FileNotFoundException("Unable to locate supplied file", file);

            var fileDirectory = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(fileDirectory))
                throw new DirectoryNotFoundException($"Unable to locate parent directory for file {file}");

            var indexFile = !string.IsNullOrEmpty(alternateIndexCacheFileLocation)
                ? alternateIndexCacheFileLocation
                : Path.Combine(fileDirectory,
                    $"{Path.GetFileName(file)}.idx");

            Debug.Assert(indexFile != null, "indexFile != null");

            if (!useCached && File.Exists(indexFile))
                File.Delete(indexFile);

            var indexExists = File.Exists(indexFile);
            useCached = useCached && indexExists;

            Indexer indexer;

            try
            {
                using (indexer = new Indexer(file, videoCodec))
                using (Observable.FromEventPattern<IndexingProgressChangeEventArgs>(
                    h => indexer.UpdateIndexProgress += h, h => indexer.UpdateIndexProgress -= h)
                    .Sample(TimeSpan.FromMilliseconds(500))
                    .Subscribe(
                        evt =>
                            RaiseIndexProgress(new IndexProgressEventArgs(evt.EventArgs.Current, evt.EventArgs.Total))))
                {
                    try
                    {
                        index = useCached ? new Index(indexFile) : indexer.Index();
                    }
                    catch (IOException wrongVersionException)
                    {
                        LastException = wrongVersionException;
                        index = indexer.Index();
                    }

                    if (useCached && !index.BelongsToFile(file))
                        index = indexer.Index();

                    Indexed = true;
                    indexedFile = file;

                    // Save index if necessary
                    if (!indexExists)
                        index.WriteIndex(indexFile);
                }
            }
            catch (Exception ex)
            {
                index = null;
                indexedFile = null;
                LastException = ex;
                return false;
            }

            return true;
        }

        public event Action<IndexProgressEventArgs> IndexProgress;

        void RaiseIndexProgress(IndexProgressEventArgs progress)
        {
            var handler = IndexProgress;
            handler?.Invoke(progress);
        }

        static Resizer CoerceResizer(FrameResizeMethod resizeMethod)
        {
            switch (resizeMethod)
            {
                case FrameResizeMethod.Area:
                    return Resizer.Area;
                case FrameResizeMethod.BicubLin:
                    return Resizer.BicubLin;
                case FrameResizeMethod.Bicubic:
                    return Resizer.Bicubic;
                case FrameResizeMethod.Bilinear:
                    return Resizer.Bilinear;
                case FrameResizeMethod.BilinearFast:
                    return Resizer.BilinearFast;
                case FrameResizeMethod.Gauss:
                    return Resizer.Gauss;
                case FrameResizeMethod.Lanczos:
                    return Resizer.Lanczos;
                case FrameResizeMethod.Point:
                    return Resizer.Point;
                case FrameResizeMethod.Sinc:
                    return Resizer.Sinc;
                case FrameResizeMethod.Spline:
                    return Resizer.Spline;
                case FrameResizeMethod.X:
                    return Resizer.X;
                default:
                    return Resizer.Bicubic;
            }
        }

        static int CoerceDisplayPixelFormat(FramePixelFormat pixelFormat)
        {
            // TODO: Look! FFMS did some of the homework for us! (implement this)
            /*
            static AVPixelFormat CSNameToPIXFMT(const char *CSName, AVPixelFormat Default) {
	            if (!CSName)
		            return FFMS_PIX_FMT(NONE);
	            std::string s = CSName;
	            std::transform(s.begin(), s.end(), s.begin(), toupper);
	            if (s == "")
		            return Default;
	            if (s == "YUV9")
		            return FFMS_PIX_FMT(YUV410P);
	            if (s == "YV411")
		            return FFMS_PIX_FMT(YUV411P);
	            if (s == "YV12")
		            return FFMS_PIX_FMT(YUV420P);
	            if (s == "YV16")
		            return FFMS_PIX_FMT(YUV422P);
	            if (s == "YV24")
		            return FFMS_PIX_FMT(YUV444P);
	            if (s == "Y8")
		            return FFMS_PIX_FMT(GRAY8);
	            if (s == "YUY2")
		            return FFMS_PIX_FMT(YUYV422);
	            if (s == "RGB24")
		            return FFMS_PIX_FMT(BGR24);
	            if (s == "RGB32")
		            return FFMS_PIX_FMT(RGB32);

	            return FFMS_PIX_FMT(NONE);
            }
            */
            switch (pixelFormat)
            {
                case FramePixelFormat.YV12:
                    return Display.PixelFormatYV12;
                case FramePixelFormat.IYUV:
                    return Display.PixelFormatIYUV;
                case FramePixelFormat.YUY2:
                    return Display.PixelFormatYUY2;
                case FramePixelFormat.RGB24:
                    return Display.PixelFormatRGB24;
                case FramePixelFormat.BGR24:
                    return Display.PixelFormatBGR24;
                default:
                    throw new ArgumentException("Unsupported pixel format", nameof(pixelFormat));
            }
        }

        public void SetSeekHandling(SeekHandling mode)
        {
            switch (mode)
            {
                case SeekHandling.LinearNoRewind:
                    seekMode = SeekMode.LinearNoRewind;
                    break;
                case SeekHandling.Linear:
                    seekMode = SeekMode.Linear;
                    break;
                case SeekHandling.Normal:
                    seekMode = SeekMode.Normal;
                    break;
                case SeekHandling.Unsafe:
                    seekMode = SeekMode.Unsafe;
                    break;
                case SeekHandling.Aggressive:
                    seekMode = SeekMode.Aggressive;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        public void SetFrameOutputFormat(int width = 0, int height = 0, FrameResizeMethod resizeMethod = FrameResizeMethod.Bicubic, FramePixelFormat pixelFormat = FramePixelFormat.None)
        {
            if (width > 0)
                frameWidth = width;
            if (height > 0)
                frameHeight = height;
            frameResizer = CoerceResizer(resizeMethod);
            if (pixelFormat != FramePixelFormat.None)
                displayPixelFormat = CoerceDisplayPixelFormat(pixelFormat);
        }

        void CheckTrack(int trackNumber)
        {
            if (!Indexed || index == null)
                throw new InvalidOperationException("No index available");

            if (trackNumber < 0 || trackNumber >= index.NumberOfTracks)
                throw new ArgumentOutOfRangeException(nameof(trackNumber), $"Track must be between 0 and {index.NumberOfTracks - 1}");
        }

        Track GetTrack(int trackNumber)
        {
            CheckTrack(trackNumber);

            var track = index.GetTrack(trackNumber);
            if (track.TrackType != TrackType.Video)
                throw new InvalidOperationException("Specified track is not a video track");

            return track;
        }

        VideoSource GetVideoSource(int trackNumber)
        {
            CheckTrack(trackNumber);
            var videoSource = videoSources.GetOrAdd(trackNumber, track =>
            {
                var source = index.VideoSource(indexedFile, track, 0, seekMode);
                // TODO: Need to make this property per video source
                var sampleFrame = source.GetFrame(0);
                SetFrameOutputFormat(sampleFrame.EncodedResolution.Width, sampleFrame.EncodedResolution.Height);
                source.SetOutputFormat(new[] {sampleFrame.EncodedPixelFormat}, frameWidth, frameHeight, frameResizer);
                return source;
            });

            return videoSource;
        }

        InteropFrameData GetFrameData(int trackNumber, int frameNumber)
        {
            var track = GetTrack(trackNumber);
            if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                throw new ArgumentOutOfRangeException(nameof(frameNumber), $"Frame must be between 0 and {track.NumberOfFrames - 1}");

            var videoSource = GetVideoSource(trackNumber);
            return new InteropFrameData(track.GetFrameInfo(frameNumber), videoSource.GetFrame(frameNumber));
        }

        public IFrame GetFrame(int trackNumber, int frameNumber)
        {
            var track = GetTrack(trackNumber);
            if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                throw new ArgumentOutOfRangeException(nameof(frameNumber), $"Frame must be between 0 and {track.NumberOfFrames - 1}");

            var frameInfo = track.GetFrameInfo(frameNumber);

            return new Frame(trackNumber, frameNumber, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame, frameInfo.RepeatPicture);
        }

        internal InternalFrame GetFrameInternal(int trackNumber, int frameNumber)
        {
            var frameData = GetFrameData(trackNumber, frameNumber);

            var frame = new InternalFrame(trackNumber, frameNumber, frameData.FrameInfo.PTS, frameData.FrameInfo.FilePos, frameData.FrameInfo.KeyFrame, frameData.FrameInfo.RepeatPicture, frameData.Frame.FrameType, frameData.Frame.EncodedResolution);
            frame.SetData(frameData.Frame.Data, frameData.Frame.DataLength);
            return frame;
        }

        public List<IFrame> GetFrames(int trackNumber)
        {
            var track = GetTrack(trackNumber);
            var infos = track.GetFrameInfos();

            return new List<IFrame>(infos.Select(i => new Frame(trackNumber, i.Frame, i.PTS, i.FilePos, i.KeyFrame, i.RepeatPicture)));
        }

        public IFrame GetFrameAtPosition(int trackNumber, long position)
        {
            var track = GetTrack(trackNumber);

            var frameInfo = track.GetFrameInfoFromPosition(position);

            return new Frame(trackNumber, frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame, frameInfo.RepeatPicture);
        }

        public IFrame GetFrameAtTime(int trackNumber, double time)
        {
            var track = GetTrack(trackNumber);
            var videoSource = GetVideoSource(trackNumber);
            // ((Time * 1000 * Frames.TB.Den) / Frames.TB.Num)
            var frameInfo = track.GetFrameInfoFromPts((long) ((time*1000*videoSource.Track.TimeBaseDenominator)/videoSource.Track.TimeBaseNumerator));

            return new Frame(trackNumber, frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame, frameInfo.RepeatPicture);
        }

        public unsafe IFrame DisplayFrame(IFrame frame, IntPtr windowId)
        {
            InternalFrame @internal = null;
            try
            {
                @internal = GetFrameInternal(frame.TrackNumber, frame.FrameNumber);
                InitDisplay(windowId);
                display.SetSize(@internal.Resolution.Width, @internal.Resolution.Height);
                display.SetPixelFormat(displayPixelFormat);

                byte*[] data =
                {
                    (byte*) @internal.Data[0].ToPointer(), (byte*) @internal.Data[1].ToPointer(), (byte*) @internal.Data[2].ToPointer(), (byte*) @internal.Data[3].ToPointer()
                };

                fixed (byte** dataPtr = data)
                fixed (int* lengthsPtr = @internal.DataLengths.ToArray())
                {
                    display.ShowFrame(dataPtr, lengthsPtr);
                }
            }
            catch (Exception)
            {
                // Ignored
            }
            return @internal == null ? frame : (Frame) @internal;
        }

        public FrameRetrievalService()
        {
            displayPixelFormat = CoerceDisplayPixelFormat(FramePixelFormat.YV12);
            FFMS2.Initialize();
            if (!FFMS2.Initialized)
                throw new Exception("Unable to initialize FFMS2");
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (disposed) return;
            if (!disposing) return;
            index?.Dispose();
            display?.Dispose();
            disposed = true;
        }

        void InitDisplay(IntPtr windowId)
        {
            if (display == null)
            {
                display = new Display(windowId);
            }
            else if (display.WindowId != windowId)
            {
                display.Dispose();
                display = new Display(windowId);
            }
        }
    }
}