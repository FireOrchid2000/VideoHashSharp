using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoHashSharp
{
    /// <summary>
    /// 等差采样,视频的第一帧必然被采样,此后每个样本帧间隔固定帧数采样
    /// </summary>
    public class ArithmeticSampler : ISampler
    {
        /// <summary>
        /// 随程序一起分发的 ffmpeg 可执行文件完整路径
        /// </summary>
        protected static readonly string FFMPEG_DIR = $@"{AppDomain.CurrentDomain.BaseDirectory}ffmpeg\bin\ffmpeg.exe";
        /// <summary>
        /// 提取视频指定索引的帧到 Stream（索引从 0 开始）
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="frameIndex">帧索引，从 0 开始</param>
        /// <param name="outputFormat">输出编码: mjpeg(默认,体积小速度快) 或 png(无损)</param>
        /// <param name="ct">取消令牌</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task<Stream> ExtractorFrameAsync(string videoFile,
            long frameIndex,
            string outputFormat = "mjpeg",
            CancellationToken ct = default
            )
        {
            if (frameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameIndex), "帧索引不能为负数");
            long frameCount = MediaHelper.GetVideoFrameCount(videoFile);
            if (frameCount > 0)
            {
                if (frameIndex > frameCount + 1)
                    throw new ArgumentOutOfRangeException(nameof(frameIndex), $"帧索引{frameIndex}超出视频帧数范围({frameCount})");
            }

            // select=eq(n,N) 精确选择第 N 帧（从0开始计数）
            // -vframes 1 仅输出一帧
            // -f image2pipe 输出到 stdout
            var psi = new ProcessStartInfo
            {
                FileName = FFMPEG_DIR,
                Arguments = $"-hide_banner -loglevel error -i \"{videoFile}\" " +
                            $"-vf \"select=eq(n\\,{frameIndex})\" -vframes 1 " +
                            $"-c:v {outputFormat} -f image2pipe -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var ms = new MemoryStream();

            // 并行读取 stdout 和 stderr
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await copyTask;
            await process.WaitForExitAsync(ct);
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg 进程失败 (退出码 {process.ExitCode}): {error}");
            }

            if (ms.Length == 0)
            {
                throw new InvalidOperationException(
                    $"未能提取第 {frameIndex} 帧，视频可能不包含该索引的帧。FFmpeg 输出: {error}");
            }

            ms.Position = 0;
            return ms;
        }


        public bool CanActHasher(Type hasherType)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Stream> Sample(string videoFile, SamplerArgs samplerArgs)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<Stream> SampleAsync(string videoFile, SamplerArgs samplerArgs, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// 等差采样参数
    /// </summary>
    public class ArithmeticArgs : SamplerArgs
    {
        /// <summary>
        /// 采样间隔
        /// </summary>
        public int Interval { get; }
        public ArithmeticArgs(int interval)
        {
            Interval = interval;
        }

        public override bool CanActSample(Type samplerType)
        {
            return samplerType.IsAssignableTo(typeof(ArithmeticSampler));
        }
    }

}
