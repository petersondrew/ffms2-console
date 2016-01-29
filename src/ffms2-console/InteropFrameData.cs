using FFMSSharp;

namespace ffms2.console
{
    internal struct InteropFrameData
    {
        public FrameInfo FrameInfo;
        public Frame Frame;

        public InteropFrameData(FrameInfo frameInfo, Frame frame )
        {
            FrameInfo = frameInfo;
            Frame = frame;
        }
    }
}