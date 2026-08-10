using System;
using MediaInfo;

namespace VideoHashSharp
{
    /// <summary>
    /// 视频媒体信息帮助类
    /// </summary>
    public static class MediaHelper
    {
        /// <summary>
        /// 获取视频文件的总帧数
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <returns>视频总帧数；无法解析时返回 -1</returns>
        public static long GetVideoFrameCount(string videoFile)
        {
            using var mediaInfo = new MediaInfo.MediaInfo();
            if (mediaInfo.Handle == IntPtr.Zero || mediaInfo.Open(videoFile) == IntPtr.Zero)
                return -1;
            string frameCount = mediaInfo.Get(StreamKind.General, 0, "FrameCount");
            return long.TryParse(frameCount, out long count) ? count : -1;
        }
    }
}
