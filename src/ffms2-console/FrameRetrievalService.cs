using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ffms2.console.ipc;
using FFMSSharp;
using Frame = ffms2.console.ipc.Frame;

namespace ffms2.console
{
    internal sealed class FrameRetrievalService : IFrameRetrievalService, IDisposable
    {
        readonly ConcurrentDictionary<int, VideoSource> videoSources = new ConcurrentDictionary<int, VideoSource>();

        bool disposed;

        string indexedFile;

        Index index;

        int framePixelFormat;

        int frameWidth, frameHeight;

        Resizer frameResizer;

        public bool Indexed { get; private set; }

        static bool UseCachedIndex(bool useCache, string path) { return useCache && File.Exists(path); }

        public bool Index(string file, bool useCached = true, string indexCacheFile = null)
        {
            Indexed = false;
            index = null;
            indexedFile = null;

            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            if (!File.Exists(file))
                throw new FileNotFoundException("Unable to locate supplied file", file);

            if (useCached && !File.Exists(indexCacheFile))
                throw new FileNotFoundException("Unable to locate supplied index cache file", indexCacheFile);

            var fileDirectory = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(fileDirectory))
                throw new DirectoryNotFoundException($"Unable to locate parent directory for file {file}");

            var indexFile = UseCachedIndex(useCached, indexCacheFile) ? indexCacheFile : Path.Combine(fileDirectory,
                $"{Path.GetFileName(file)}.idx");
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

        public event Action<IndexProgressEventArgs> IndexProgress;

        void OnIndexProgress(object sender, IndexingProgressChangeEventArgs eventArgs) { RaiseIndexProgress(new IndexProgressEventArgs(eventArgs.Current, eventArgs.Total)); }

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

        public void SetFrameOutputFormat(int width, int height, FrameResizeMethod resizeMethod)
        {
            // BGRA is required for bitmap export
            framePixelFormat = FFMS2.GetPixelFormat("bgra");
            frameWidth = width;
            frameHeight = height;
            frameResizer = CoerceResizer(resizeMethod);
        }

        void CheckTrack(int trackNumber)
        {
            if (!Indexed || index == null)
                throw new InvalidOperationException("No index available");

            if (trackNumber < 0 || trackNumber >= index.NumberOfTracks)
                throw new ArgumentOutOfRangeException(nameof(trackNumber),
                    $"Track must be between 0 and {index.NumberOfTracks - 1}");
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
                var source = index.VideoSource(indexedFile, track, 0);
                // TODO: Need to make this property per video source
                var sampleFrame = source.GetFrame(0);
                // BGRA is required for bitmap export
                SetFrameOutputFormat(sampleFrame.EncodedResolution.Width, sampleFrame.EncodedResolution.Height, FrameResizeMethod.Bicubic);
                source.SetOutputFormat(new[] { framePixelFormat }, frameWidth, frameHeight, frameResizer);
                //source.SetOutputFormat(new[] { sampleFrame.EncodedPixelFormat }, frameWidth, frameHeight, frameResizer);
                return source;
            });

            return videoSource;
        }

        public IFrame GetFrame(int trackNumber, int frameNumber)
        {
            var track = GetTrack(trackNumber);
            if (frameNumber < 0 || frameNumber >= track.NumberOfFrames)
                throw new ArgumentOutOfRangeException(nameof(frameNumber),
                    $"Frame must be between 0 and {track.NumberOfFrames - 1}");

            var frameInfo = track.GetFrameInfo(frameNumber);
            var videoSource = GetVideoSource(trackNumber);
            var extractedFrame = videoSource.GetFrame(frameNumber);

            return new Frame(frameNumber, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame, frameInfo.RepeatPicture,
                extractedFrame.FrameType, extractedFrame.Resolution, extractedFrame.Data[0],
                extractedFrame.DataLength[0]);
        }

        public List<IFrame> GetFrameInfos(int trackNumber)
        {
            var track = GetTrack(trackNumber);
            var infos = track.GetFrameInfos();

            return new List<IFrame>(infos.Select(i => new Frame(i.Frame, i.PTS, i.FilePos, i.KeyFrame, i.RepeatPicture)));
        }

        public IFrame GetFrameAtPosition(int trackNumber, long position)
        {
            var track = GetTrack(trackNumber);

            var frameInfo = track.GetFrameInfoFromPosition(position);
            var videoSource = GetVideoSource(trackNumber);
            var extractedFrame = videoSource.GetFrameByPosition(position);

            return new Frame(frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame,
                frameInfo.RepeatPicture, extractedFrame.FrameType, extractedFrame.Resolution, extractedFrame.Data[0],
                extractedFrame.DataLength[0]);
        }

        public IFrame GetFrameAtTime(int trackNumber, double time)
        {
            var track = GetTrack(trackNumber);
            var videoSource = GetVideoSource(trackNumber);
            // ((Time * 1000 * Frames.TB.Den) / Frames.TB.Num)
            var frameInfo = track.GetFrameInfoFromPts((long) ((time * 1000 * videoSource.Track.TimeBaseDenominator) / videoSource.Track.TimeBaseNumerator));
            var extractedFrame = videoSource.GetFrameByTime(time);

            return new Frame(frameInfo.Frame, frameInfo.PTS, frameInfo.FilePos, frameInfo.KeyFrame,
                frameInfo.RepeatPicture, extractedFrame.FrameType, extractedFrame.Resolution, extractedFrame.Data[0],
                extractedFrame.DataLength[0]);
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
            if (!disposing) return;
            index?.Dispose();
            disposed = true;
        }
    }
}