using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using ffms2.console.ipc;
using ffms2.console.ipc.Exceptions;
using FFMSSharp;
using sdldisplay;
using Frame = ffms2.console.ipc.Frame;
using FFMSTrack = FFMSSharp.Track;
using FFMSTrackType = FFMSSharp.TrackType;
using Track = ffms2.console.ipc.Track;

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
                    catch (IOException)
                    {
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
                throw new IndexCreationException($"Error creating index for file {file}", ex);
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

        public void SetFrameOutputFormat(int width = 0, int height = 0,
            FrameResizeMethod resizeMethod = FrameResizeMethod.Bicubic,
            FramePixelFormat pixelFormat = FramePixelFormat.None)
        {
            if (width > 0)
                frameWidth = width;
            if (height > 0)
                frameHeight = height;
            frameResizer = CoerceResizer(resizeMethod);
            if (pixelFormat != FramePixelFormat.None)
                displayPixelFormat = CoerceDisplayPixelFormat(pixelFormat);
        }

        public List<ITrack> GetTracks()
        {
            if (!Indexed || index == null)
                throw new InvalidOperationException("No index available");

            var tracks = new List<ITrack>();
            for (var t = 0; t < index.NumberOfTracks; t++)
            {
                var ffmsTrack = index.GetTrack(t);
                if (ffmsTrack == null) continue;
                tracks.Add(new Track(t, (ipc.TrackType) ffmsTrack.TrackType, ffmsTrack.NumberOfFrames,
                    new TimeBase(ffmsTrack.TimeBaseNumerator, ffmsTrack.TimeBaseDenominator)));
            }
            return tracks;
        }

        void CheckTrack(int trackNumber)
        {
            if (!Indexed || index == null)
                throw new InvalidOperationException("No index available");

            if (trackNumber < 0 || trackNumber >= index.NumberOfTracks)
                throw new ArgumentOutOfRangeException(nameof(trackNumber),
                    $"Track must be between 0 and {index.NumberOfTracks - 1}");
        }

        FFMSTrack GetTrack(int trackNumber)
        {
            try
            {
                CheckTrack(trackNumber);

                var track = index.GetTrack(trackNumber);
                if (track.TrackType != FFMSTrackType.Video)
                    throw new InvalidOperationException("Specified track is not a video track");

                return track;
            }
            catch (Exception ex)
            {
                throw new IndexAccessException($"Unable to retrieve track {trackNumber} from index", ex);
            }
        }

        VideoSource GetVideoSource(int trackNumber)
        {
            try
            {
                CheckTrack(trackNumber);
                var videoSource = videoSources.GetOrAdd(trackNumber, track =>
                {
                    var source = index.VideoSource(indexedFile, track, 0, seekMode);
                    // TODO: Need to make this property per video source
                    var sampleFrame = source.GetFrame(0);
                    SetFrameOutputFormat(sampleFrame.EncodedResolution.Width, sampleFrame.EncodedResolution.Height);
                    source.SetOutputFormat(new[] { sampleFrame.EncodedPixelFormat }, frameWidth, frameHeight, frameResizer);
                    return source;
                });

                return videoSource;
            }
            catch (Exception ex)
            {
                throw new FFMSException("Error setting up or retrieving cached videosource", ex);
            }
        }

        InteropFrameData GetFrameData(int trackNumber, int frameNumber)
        {
            FrameInfo info;
            try
            {
                var track = GetTrack(trackNumber);
                if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                    throw new ArgumentOutOfRangeException(nameof(frameNumber),
                        $"Frame must be between 0 and {track.NumberOfFrames - 1}");
                info = track.GetFrameInfo(frameNumber);
            }
            catch (Exception ex)
            {
                throw new IndexAccessException(frameNumber, trackNumber, ex);
            }

            try
            {
                var videoSource = GetVideoSource(trackNumber);
                var frame = videoSource.GetFrame(frameNumber);
                return new InteropFrameData(info, frame);
            }
            catch (Exception ex)
            {
                throw new FrameRetrievalException(
                    $"Error retrieving frame {frameNumber} in track {trackNumber} from videosource", ex);
            }

        }

        public IFrame GetFrame(int trackNumber, int frameNumber)
        {
            try
            {
                var track = GetTrack(trackNumber);
                if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                    throw new ArgumentOutOfRangeException(nameof(frameNumber),
                        $"Frame must be between 0 and {track.NumberOfFrames - 1}");

                var frameInfo = track.GetFrameInfo(frameNumber);

                return new Frame(trackNumber, frameNumber, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame,
                    frameInfo.RepeatPicture);
            }
            catch (Exception ex)
            {
                throw new IndexAccessException(frameNumber, trackNumber, ex);
            }
        }

        internal InternalFrame GetFrameInternal(int trackNumber, int frameNumber)
        {
            var frameData = GetFrameData(trackNumber, frameNumber);

            var frame = new InternalFrame(trackNumber, frameNumber, frameData.FrameInfo.PTS, frameData.FrameInfo.FilePos,
                frameData.FrameInfo.KeyFrame, frameData.FrameInfo.RepeatPicture, frameData.Frame.FrameType,
                frameData.Frame.EncodedResolution);
            frame.SetData(frameData.Frame.Data, frameData.Frame.DataLength);
            return frame;
        }

        public List<IFrame> GetFrames(int trackNumber)
        {
            try
            {
                var track = GetTrack(trackNumber);
                var infos = track.GetFrameInfos();

                return
                    new List<IFrame>(
                        infos.Select(i => new Frame(trackNumber, i.Frame, i.PTS, i.FilePos, i.KeyFrame, i.RepeatPicture)));
            }
            catch (Exception ex)
            {
                throw new IndexAccessException($"Error retrieving frames in track {trackNumber} from index", ex);
            }
        }

        public IFrame GetFrameAtPosition(int trackNumber, long position)
        {
            try
            {
                var track = GetTrack(trackNumber);

                var frameInfo = track.GetFrameInfoFromPosition(position);

                return new Frame(trackNumber, frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame,
                    frameInfo.RepeatPicture);
            }
            catch (Exception ex)
            {
                throw new IndexAccessException(position, trackNumber, ex);
            }
        }

        public IFrame GetFrameAtTime(int trackNumber, double time)
        {
            try
            {
                var track = GetTrack(trackNumber);
                var videoSource = GetVideoSource(trackNumber);
                // ((Time * 1000 * Frames.TB.Den) / Frames.TB.Num)
                var frameInfo =
                    track.GetFrameInfoFromPts(
                        (long) ((time*1000*videoSource.Track.TimeBaseDenominator)/videoSource.Track.TimeBaseNumerator));

                return new Frame(trackNumber, frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame,
                    frameInfo.RepeatPicture);
            }
            catch (Exception ex)
            {
                throw new IndexAccessException(time, trackNumber, ex);
            }
        }

        public unsafe IFrame DisplayFrame(IFrame frame, IntPtr windowId)
        {
            var @internal = GetFrameInternal(frame.TrackNumber, frame.FrameNumber);

            try
            {
                InitDisplay(windowId);
                display.SetSize(@internal.Resolution.Width, @internal.Resolution.Height);
                display.SetPixelFormat(displayPixelFormat);

                byte*[] data =
                {
                    (byte*) @internal.Data[0].ToPointer(), (byte*) @internal.Data[1].ToPointer(),
                    (byte*) @internal.Data[2].ToPointer(), (byte*) @internal.Data[3].ToPointer()
                };

                fixed (byte** dataPtr = data)
                fixed (int* lengthsPtr = @internal.DataLengths.ToArray())
                {
                    display.ShowFrame(dataPtr, lengthsPtr);
                }
            }
            catch (Exception ex)
            {
                if (ex is FatalDisplayException || ex is AccessViolationException)
                {
                    // Re-init on next call
                    display.Dispose();
                    display = null;
                    throw new FrameDisplayException(
                        $"A fatal exception occurred while attempting to display frame {frame.FrameNumber} and SDL was re-initialized");
                }
                throw new FrameDisplayException(
                    $"An exception occurred while attempting to display frame {frame.FrameNumber}", ex);
            }
            return (Frame) @internal;
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