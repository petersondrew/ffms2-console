using System;
using System.Collections.ObjectModel;
using System.Drawing;
using ffms2.console.ipc;

namespace ffms2.console
{
    internal class InternalFrame : IFrame
    {
        public int TrackNumber { get; }
        public int FrameNumber { get; }
        public long PTS { get; }
        public long FilePos { get; }
        public bool KeyFrame { get; }
        public int RepeatPicture { get; }
        public char FrameType { get; }
        public Size Resolution { get; }
        public ReadOnlyCollection<IntPtr> Data { get; private set; }
        public ReadOnlyCollection<int> DataLengths { get; private set; }

        public InternalFrame()
        {
        }

        public InternalFrame(int trackNumber, int frameNumber, long pts, long filePos, bool keyFrame, int repeatPicture)
        {
            TrackNumber = trackNumber;
            FrameNumber = frameNumber;
            PTS = pts;
            FilePos = filePos;
            KeyFrame = keyFrame;
            RepeatPicture = repeatPicture;
        }

        public InternalFrame(int trackNumber, int frameNumber, long pts, long filePos, bool keyFrame, int repeatPicture,
            char frameType,
            Size resolution) : this(trackNumber, frameNumber, pts, filePos, keyFrame, repeatPicture)
        {
            FrameType = frameType;
            Resolution = resolution;
        }

        public void SetData(ReadOnlyCollection<IntPtr> data, ReadOnlyCollection<int> dataLengths)
        {
            Data = data;
            DataLengths = dataLengths;
        }

        public static implicit operator Frame(InternalFrame @internal)
        {
            return new Frame(@internal.TrackNumber, @internal.FrameNumber, @internal.PTS, @internal.FilePos,
                @internal.KeyFrame,
                @internal.RepeatPicture,
                @internal.FrameType, @internal.Resolution);
        }

        public static implicit operator InternalFrame(Frame frame)
        {
            return new InternalFrame(frame.TrackNumber, frame.FrameNumber, frame.PTS, frame.FilePos, frame.KeyFrame,
                frame.RepeatPicture,
                frame.FrameType, frame.Resolution);
        }
    }
}
