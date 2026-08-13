using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VideoHashSharp
{

    /// <summary>
    /// 帧提取器：封装对 ffmpeg 进程的调用，从视频文件中提取指定索引的帧。
    /// 支持单帧提取与单进程批量提取，并通过 image2pipe 输出 + 帧边界切分将字节流还原为独立帧 Stream
    /// </summary>
    internal static class FrameExtractor
    {
        /// <summary>
        /// 单次 ffmpeg 进程提取的帧数上限，超过此数量的帧会分多批处理，以控制内存占用
        /// </summary>
        public const int BatchFrameCount = 64;

        /// <summary>
        /// 随程序一起分发的 ffmpeg 可执行文件完整路径
        /// </summary>
        private static readonly string FFMPEG_DIR = $@"{AppDomain.CurrentDomain.BaseDirectory}ffmpeg\bin\ffmpeg.exe";

        /// <summary>
        /// PNG 文件签名
        /// </summary>
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        /// <summary>
        /// 提取视频指定索引的帧到 Stream（索引从 0 开始）
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="frameIndex">帧索引，从 0 开始</param>
        /// <param name="outputFormat">输出编码: png(默认,无损,可按 chunk 精确切分) 或 mjpeg(体积小速度快)</param>
        /// <param name="ct">取消令牌</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task<Stream> ExtractorFrameAsync(string videoFile,
            long frameIndex,
            string outputFormat = "png",
            CancellationToken ct = default
            )
        {
            if (frameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameIndex), "帧索引不能为负数");

            var frames = await ExtractFramesAsync(videoFile, [frameIndex], outputFormat, ct);
            if (frames.Count == 0)
                throw new InvalidOperationException($"未能提取第 {frameIndex} 帧，视频可能不包含该索引的帧");
            return frames[0];
        }

        /// <summary>
        /// 单次 ffmpeg 进程批量提取视频指定索引的帧（索引从 0 开始），返回的每个 Stream 为独立的一帧图片数据
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="frameIndexes">帧索引序列，从 0 开始</param>
        /// <param name="outputFormat">输出编码: png(默认) 或 mjpeg</param>
        /// <param name="ct">取消令牌</param>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task<List<Stream>> ExtractFramesAsync(string videoFile,
            IEnumerable<long> frameIndexes,
            string outputFormat = "png",
            CancellationToken ct = default
            )
        {
            var indexes = frameIndexes.ToList();
            if (indexes.Count == 0)
                return [];

            // select=eq(n,N1)+eq(n,N2)+... 精确选择多个帧；
            // 表达式须用单引号包裹：Windows 下滤镜内的反斜杠转义（\,）会被参数解析破坏，引号内的逗号不会被当作滤镜分隔符
            string select = $"select='{string.Join("+", indexes.Select(i => $"eq(n,{i})"))}'";

            // 构造 ffmpeg 进程启动参数
            var psi = new ProcessStartInfo
            {
                FileName = FFMPEG_DIR,
                // -hide_banner 屏蔽版本横幅；-loglevel error 仅输出错误；
                // -vsync 0 关闭帧率对齐，否则 select 丢弃帧后 ffmpeg 会用复制帧填充输出流
                Arguments = $"-hide_banner -loglevel error -i \"{videoFile}\" " +
                            $"-vf \"{select}\" -vsync 0 " +
                            $"-c:v {outputFormat} -f image2pipe -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // 并行读取 stdout 与 stderr：stdout 为连续拼接的帧图片字节流，stderr 用于捕获错误信息
            var stdout = new MemoryStream();
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await copyTask;
            await process.WaitForExitAsync(ct);
            string error = await errorTask;

            // 非零退出码表示 ffmpeg 执行失败（如输入不存在、滤镜参数非法等），携带 stderr 抛出
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg 进程失败 (退出码 {process.ExitCode}): {error}");
            }

            // 按输出格式选择对应的帧切分策略还原独立帧
            var bytes = stdout.ToArray();
            return outputFormat.Equals("png", StringComparison.OrdinalIgnoreCase)
                ? SplitPngStream(bytes)
                : SplitJpegStream(bytes);
        }

        /// <summary>
        /// 将连续拼接的 PNG 帧流按 chunk 长度精确切分为独立的帧 Stream。
        /// 从每帧签名处开始按 PNG chunk 结构遍历（长度字段 + 类型 + 数据 + CRC），
        /// 遇 IEND 即帧结束，因此图像数据中的任意字节序列都不会被误判为帧边界
        /// </summary>
        private static List<Stream> SplitPngStream(byte[] bytes)
        {
            var frames = new List<Stream>();
            int pos = 0;
            while (true)
            {
                // 从上一帧结束位置向后定位下一帧的 PNG 签名
                int start = -1;
                for (int j = pos; j + PngSignature.Length <= bytes.Length; j++)
                {
                    if (bytes.AsSpan(j, PngSignature.Length).SequenceEqual(PngSignature))
                    {
                        start = j;
                        break;
                    }
                }
                if (start < 0)
                    break;

                // 从签名后按 chunk 结构遍历：长度(4 字节大端) + 类型(4) + 数据 + CRC(4)，遇 IEND 即帧结束
                int p = start + PngSignature.Length;
                int frameEnd = -1;
                while (p + 8 <= bytes.Length)
                {
                    int len = (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
                    string type = Encoding.ASCII.GetString(bytes, p + 4, 4);
                    p += 8 + len + 4;
                    if (type == "IEND")
                    {
                        frameEnd = p;
                        break;
                    }
                }
                if (frameEnd < 0)
                    break;

                // 复制独立帧字节到新数组，避免各帧共享大缓冲区
                var segment = new byte[frameEnd - start];
                Array.Copy(bytes, start, segment, 0, segment.Length);
                frames.Add(new MemoryStream(segment));
                pos = frameEnd;
            }
            return frames;
        }

        /// <summary>
        /// 将连续拼接的 JPEG 字节流按帧起始标记 0xFF 0xD8 切分为独立的帧 Stream。
        /// 注意：JPEG 的 DQT/DHT 段数据可能包含 0xFF 0xD8 序列导致误判，仅适用于单帧等低误判风险场景
        /// </summary>
        private static List<Stream> SplitJpegStream(byte[] bytes)
        {
            // 第一遍扫描：收集所有帧起始标记 0xFF 0xD8 的位置
            var starts = new List<int>();
            for (int j = 0; j + 1 < bytes.Length; j++)
            {
                if (bytes[j] == 0xFF && bytes[j + 1] == 0xD8)
                    starts.Add(j);
            }

            // 第二遍：按相邻起始标记切分，最后一帧截取到字节流末尾
            var frames = new List<Stream>(starts.Count);
            for (int k = 0; k < starts.Count; k++)
            {
                int start = starts[k];
                int end = k + 1 < starts.Count ? starts[k + 1] : bytes.Length;
                var segment = new byte[end - start];
                Array.Copy(bytes, start, segment, 0, segment.Length);
                frames.Add(new MemoryStream(segment));
            }
            return frames;
        }
    }

    /// <summary>
    /// 等差采样,视频的第一帧必然被采样,此后每 Interval 帧（索引差）采样一次
    /// </summary>
    public class ArithmeticSampler : ISampler
    {
        /// <summary>
        /// 返回当前采样器是否可以作用于指定类型的哈希器。
        /// 等差采样产出的是通用图片帧流，因此任何实现了 <see cref="IHasher"/> 的哈希器均可使用
        /// </summary>
        /// <param name="hasherType">被测试的哈希器类型</param>
        /// <returns>恒为 true（hasherType 实现了 IHasher 时）</returns>
        public bool CanActHasher(Type hasherType)
        {
            return hasherType.IsAssignableTo(typeof(IHasher));
        }

        /// <summary>
        /// 同步执行等差采样，返回样本帧数据流的迭代器
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="samplerArgs">采样参数，必须为 <see cref="ArithmeticArgs"/> 类型</param>
        /// <returns>样本帧数据流的迭代器</returns>
        /// <exception cref="ArgumentException">采样参数与采样器类型不匹配时抛出</exception>
        public IEnumerable<Stream> Sample(string videoFile, SamplerArgs samplerArgs)
        {
            if (!samplerArgs.CanActSample(this.GetType()))
                throw new ArgumentException($"类型为{this.GetType().Name}的采样器无法处理类型为{samplerArgs.GetType().Name}的采样参数");

            var args = (ArithmeticArgs)samplerArgs;

            // 解析视频总帧数；无法解析时返回 -1，循环不执行从而返回空序列
            var frameCount = MediaHelper.GetVideoFrameCount(videoFile);
            // 外层循环按批推进：每批最多 BatchFrameCount 个采样帧，步长为 批大小 × 采样间隔
            for (long start = 0; start < frameCount; start += FrameExtractor.BatchFrameCount * args.Interval)
            {
                // 收集本批内所有采样帧索引
                var indexes = new List<long>();
                for (long i = start; i < frameCount && indexes.Count < FrameExtractor.BatchFrameCount; i += args.Interval)
                    indexes.Add(i);

                // 同步方法复用异步提取，GetAwaiter().GetResult() 直接传播原始异常而不包装为 AggregateException
                var frames = FrameExtractor.ExtractFramesAsync(videoFile, indexes).GetAwaiter().GetResult();
                foreach (var frame in frames)
                    yield return frame;
            }
        }

        /// <summary>
        /// 异步执行等差采样，返回样本帧数据流的异步迭代器
        /// </summary>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="samplerArgs">采样参数，必须为 <see cref="ArithmeticArgs"/> 类型</param>
        /// <param name="ct">取消令牌，传递到底层帧提取进程</param>
        /// <returns>样本帧数据流的异步迭代器</returns>
        /// <exception cref="ArgumentException">采样参数与采样器类型不匹配时抛出</exception>
        public async IAsyncEnumerable<Stream> SampleAsync(string videoFile, SamplerArgs samplerArgs, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!samplerArgs.CanActSample(this.GetType()))
                throw new ArgumentException($"类型为{this.GetType().Name}的采样器无法处理类型为{samplerArgs.GetType().Name}的采样参数");

            var args = (ArithmeticArgs)samplerArgs;

            // 解析视频总帧数；无法解析时返回 -1，循环不执行从而返回空序列
            var frameCount = MediaHelper.GetVideoFrameCount(videoFile);
            // 外层循环按批推进：每批最多 BatchFrameCount 个采样帧，步长为 批大小 × 采样间隔
            for (long start = 0; start < frameCount; start += FrameExtractor.BatchFrameCount * args.Interval)
            {
                // 收集本批内所有采样帧索引
                var indexes = new List<long>();
                for (long i = start; i < frameCount && indexes.Count < FrameExtractor.BatchFrameCount; i += args.Interval)
                    indexes.Add(i);

                // 逐批异步提取，取消令牌透传给 ffmpeg 进程
                var frames = await FrameExtractor.ExtractFramesAsync(videoFile, indexes, ct: ct);
                foreach (var frame in frames)
                    yield return frame;
            }
        }
    }
    /// <summary>
    /// 等差采样参数
    /// </summary>
    public class ArithmeticArgs : SamplerArgs
    {
        /// <summary>
        /// 相邻采样帧的索引之差（即两采样帧之间跳过的帧数为 Interval - 1，Interval 为 1 时逐帧采样）
        /// </summary>
        public long Interval { get; private set; }
        /// <summary>
        /// 构造等差采样参数
        /// </summary>
        /// <param name="interval">采样间隔（相邻采样帧索引之差），必须大于 0</param>
        /// <exception cref="ArgumentOutOfRangeException">interval 不大于 0 时抛出（0 会导致死循环）</exception>
        public ArithmeticArgs(long interval)
        {
            if (interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval), "采样间隔必须大于 0");
            Interval = interval;
        }

        /// <summary>
        /// 根据采样百分比计算视频文件采样间隔
        /// </summary>
        /// <param name="percent">要采集视频<paramref name="videofile"/>中帧数的百分比0.0f到1.0f</param>
        /// <param name="videofile">要采集的视频文件</param>
        /// <returns>采样间隔</returns>
        public static long GetInterval(float percent, string videofile)
        {
            if (percent < 0.0f || percent > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(percent), "采样百分比必须在 0.0f 到 1.0f 之间");

            if(!File.Exists(videofile))
                throw new FileNotFoundException(videofile);

            var tal = MediaHelper.GetVideoFrameCount(videofile);
            var n = (long)(tal * percent);
            return tal / n;
        }

        /// <summary>
        /// 此采样参数是否可作用于指定类型的采样器：仅 <see cref="ArithmeticSampler"/> 类型返回 true
        /// </summary>
        /// <param name="samplerType">被测试的采样器类型</param>
        /// <returns>samplerType 为 ArithmeticSampler 或其派生类型时返回 true</returns>
        public override bool CanActSample(Type samplerType)
        {
            return samplerType.IsAssignableTo(typeof(ArithmeticSampler));
        }
    }

}
