using System;
using System.IO;
using ffms2.console.ipc;
using FFMSSharp;
using Frame = ffms2.console.ipc.Frame;

namespace ffms2.console
{
    internal sealed class FrameRetrievalService : IFrameRetrievalService, IDisposable
    {
        bool disposed;

        string indexedFile;

        Index index;

        int framePixelFormat;

        int frameWidth, frameHeight;

        Resizer frameResizer;

        bool frameOutputFormatSet;

        public bool Indexed { get; private set; }

        static bool UseCachedIndex(bool useCache, string path) { return useCache && File.Exists(path); }

        public bool Index(string file, bool useCached = true, string indexCacheFile = null)
        {
            Indexed = false;
            index = null;
            indexedFile = null;

            if (String.IsNullOrEmpty(file))
                throw new ArgumentNullException("file");

            if (!File.Exists(file))
                throw new FileNotFoundException("Unable to locate supplied file", file);

            if (useCached && !File.Exists(file))
                throw new FileNotFoundException("Unable to locate supplied index cache file", indexCacheFile);

            var fileDirectory = Path.GetDirectoryName(file);
            if (String.IsNullOrEmpty(fileDirectory))
                throw new DirectoryNotFoundException(String.Format("Unable to locate parent directory for file {0}", file));

            var indexFile = UseCachedIndex(useCached, indexCacheFile) ? indexCacheFile : Path.Combine(fileDirectory, String.Format("{0}.idx", Path.GetFileName(file)));
            var indexExists = File.Exists(indexFile);

            Indexer indexer = null;

            try
            {
                using (indexer = new Indexer(file))
                {
                    indexer.UpdateIndexProgress += OnIndexProgress;
                    index = indexExists ? new Index(indexFile) : indexer.Index();

                    if (useCached && !index.BelongsToFile(file))
                        throw new IndexMismatchException("Supplied index does not match the file", file, indexCacheFile);

                    Indexed = true;
                    indexedFile = file;
                    indexer.UpdateIndexProgress -= OnIndexProgress;

                    // Save index if necessary
                    if (!indexExists)
                        index.WriteIndex(indexFile);
                }
            }
            catch (Exception)
            {
                index = null;
                indexedFile = null;
                if (indexer != null)
                    indexer.UpdateIndexProgress -= OnIndexProgress;
                throw;
            }

            return true;
        }

        public event EventHandler<IndexProgressEventArgs> IndexProgress;

        void OnIndexProgress(object sender, IndexingProgressChangeEventArgs eventArgs) { RaiseIndexProgress(new IndexProgressEventArgs(eventArgs.Current, eventArgs.Total)); }

        void RaiseIndexProgress(IndexProgressEventArgs progress)
        {
            var handler = IndexProgress;
            if (handler != null)
            {
                handler(this, progress);
            }
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

        public void SetFrameOutputFormat(int width, int height, FrameResizeMethod resizeMethod)
        {
            // BGRA is required for bitmap export
            framePixelFormat = FFMS2.GetPixelFormat("bgra");
            frameWidth = width;
            frameHeight = height;
            frameResizer = CoerceResizer(resizeMethod);
            frameOutputFormatSet = true;
        }

        void CheckTrack(int trackNumber)
        {
            if (!Indexed || index == null)
                throw new InvalidOperationException("No index available");

            if (trackNumber < 0 || trackNumber >= index.NumberOfTracks)
                throw new ArgumentOutOfRangeException("trackNumber", String.Format("Track must be between 0 and {0}", index.NumberOfTracks - 1));
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

            var videoSource = index.VideoSource(indexedFile, trackNumber, 0);
            if (!frameOutputFormatSet)
            {
                // Get first frame properties to start with
                var sampleFrame = videoSource.GetFrame(0);
                // BGRA is required for bitmap export
                SetFrameOutputFormat(sampleFrame.EncodedResolution.Width, sampleFrame.EncodedResolution.Height, FrameResizeMethod.Bicubic);
            }
            videoSource.SetOutputFormat(new[] {framePixelFormat}, frameWidth, frameHeight, frameResizer);
            return videoSource;
        }

        public IFrame GetFrame(int trackNumber, int frameNumber)
        {
            var track = GetTrack(trackNumber);
            if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                throw new ArgumentOutOfRangeException("frameNumber", String.Format("Frame must be between 0 and {0}", track.NumberOfFrames - 1));

            var frameInfo = track.GetFrameInfo(frameNumber);
            var videoSource = GetVideoSource(trackNumber);
            var extractedFrame = videoSource.GetFrame(frameNumber);

            return new Frame(frameNumber, frameInfo.PTS, extractedFrame.FrameType, extractedFrame.Bitmap);
        }

        public IFrame GetFrameAtPosition(int trackNumber, long position)
        {
            var track = GetTrack(trackNumber);

            var frameInfo = track.GetFrameInfoFromPosition(position);
            var videoSource = GetVideoSource(trackNumber);
            var extractedFrame = videoSource.GetFrameByPosition(position);

            return new Frame(frameInfo.Frame, frameInfo.PTS, extractedFrame.FrameType, extractedFrame.Bitmap);
        }

        public IFrame GetFrameAtTime(int trackNumber, double time)
        {
            var track = GetTrack(trackNumber);
            var videoSource = GetVideoSource(trackNumber);
            // ((Time * 1000 * Frames.TB.Den) / Frames.TB.Num)
            var frameInfo = track.GetFrameInfoFromPts((long) ((time * 1000 * videoSource.Track.TimeBaseDenominator) / videoSource.Track.TimeBaseNumerator));
            var extractedFrame = videoSource.GetFrameByTime(time);

            return new Frame(frameInfo.Frame, frameInfo.PTS, extractedFrame.FrameType, extractedFrame.Bitmap);
        }

        public FrameRetrievalService()
        {
            FFMS2.Initialize();
            if (!FFMS2.Initialized)
                throw new Exception("Unable to initialize FFMS2");
        }

        public void Dispose() { Dispose(true); }

        void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                if (index != null)
                    index.Dispose();
                disposed = true;
            }
        }
    }
}