using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace VideoHashSharp
{
    /// <summary>
    /// 多数表决算法实现的哈希融合器
    /// </summary>
    public class MajorityVoteMerger : IHashMerger
    {
        /// <summary>
        /// 将多个哈希融合为一个哈希：对每个比特位统计全部源哈希中该位的取票数，
        /// 超过半数的置 1，否则置 0（平局取 0）；
        /// 结果长度取最长哈希的长度，较短的哈希缺失字节视为 0。
        /// 相比逐字节平均，该算法不产生源哈希中不存在的中间值，更贴合按汉明距离比较的位哈希语义。
        /// </summary>
        /// <param name="hashs">多个内容哈希</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public byte[] Merge(IEnumerable<byte[]> hashs)
        {
            // 参数空引用校验：当 hashs 为 null 时抛出 ArgumentNullException，防止后续空引用访问
            ArgumentNullException.ThrowIfNull(hashs, nameof(hashs));

            // 用集合表达式将惰性枚举的序列一次性物化为数组，避免后续对同一序列执行多次枚举
            byte[][] array = [.. hashs];
            // 空输入场景：没有任何源哈希可融合，直接返回空哈希作为融合结果
            if (array.Length == 0)
            {
                return [];
            }

            // 结果长度取所有源哈希的最大长度，保证不丢失最长哈希的任何字节信息
            int length = array.Max(h => h.Length);

            // 按最大长度预分配结果字节数组，数组元素默认初始化为 0
            byte[] result = new byte[length];

            // 外层循环：依次处理结果哈希中的每个字节位置 i
            for (int i = 0; i < length; i++)
            {
                // 中层循环：依次处理当前字节的每个比特位（bit 取 0~7，对应从最低位到最高位）
                for (int bit = 0; bit < 8; bit++)
                {
                    // ones 记录全部源哈希中当前比特位取值为 1 的个数（即"赞成票"数）
                    int ones = 0;
                    // 内层循环：遍历所有源哈希，统计当前比特位的赞成票数
                    for (int k = 0; k < array.Length; k++)
                    {
                        // 取第 k 个源哈希的第 i 个字节；若该哈希长度不足 i+1，则缺失字节按 0 参与表决
                        byte b = i < array[k].Length ? array[k][i] : (byte)0;
                        // 用按位与判断该字节的当前比特位是否为 1：结果非 0 说明该位是 1，计入赞成票
                        if ((b & (1 << bit)) != 0)
                        {
                            ones++;
                        }
                    }
                    // 多数表决判定：赞成票数严格超过总数一半（ones * 2 > 总数）才置 1；
                    // 偶数个源哈希出现平局时保持 0，避免随意偏向某一方
                    if (ones * 2 > array.Length)
                    {
                        // 把结果字节的当前比特位置 1（|= 只影响目标位，不影响其余位）
                        result[i] |= (byte)(1 << bit);
                    }
                }
            }

            // 返回逐位多数表决后的融合哈希
            return result;
        }
    }

    public static class UlongExtensions
    {
        /// <summary>
        /// 将 ulong 哈希值转换为大端序的字节数组（8 字节）
        /// </summary>
        /// <param name="value">ulong 哈希值</param>
        /// <returns>大端序字节数组</returns>
        public static byte[] ToBytes(this ulong value)
        {
            // BitConverter.GetBytes 返回当前平台字节序（本机为小端）
            byte[] bytes = BitConverter.GetBytes(value);
            // 小端平台需反转字节序，统一按大端序输出，保证跨平台字节顺序一致
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        /// <summary>
        /// 将大端序字节数组转换为 ulong 哈希值
        /// </summary>
        /// <param name="bytes">大端序字节数组，长度必须为 8</param>
        /// <returns>ulong 哈希值</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static ulong ToUlong(this byte[] bytes)
        {
            // 参数空引用校验
            ArgumentNullException.ThrowIfNull(bytes, nameof(bytes));
            // ulong 固定为 8 字节，长度不符时无法转换
            if (bytes.Length != sizeof(ulong))
            {
                throw new ArgumentException($"字节数组长度必须为 {sizeof(ulong)}", nameof(bytes));
            }
            // 小端平台需先反转再调用 BitConverter，与 ToBytes 的大端序约定保持一致
            if (BitConverter.IsLittleEndian)
            {
                byte[] reversed = (byte[])bytes.Clone();
                Array.Reverse(reversed);
                return BitConverter.ToUInt64(reversed);
            }
            return BitConverter.ToUInt64(bytes);
        }
    }

    /// <summary>
    /// 平均算法实现的哈希融合器
    /// </summary>
    public class AveHashMegerger : IHashMerger
    {
        /// <summary>
        /// 将两个内容哈希融合为一个哈希，融合结果长度取两者较长者；
        /// 每字节按加权平均（四舍五入）合并，较短的哈希缺失字节视为 0，
        /// 使得融合哈希同时保留两个源哈希的内容特征
        /// <br></br>满足幂等率
        /// </summary>
        /// <param name="hash1">第一个内容哈希</param>
        /// <param name="hash2">第二个内容哈希</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal static byte[] Merge(byte[] hash1, byte[] hash2)
        {
            ArgumentNullException.ThrowIfNull(hash1, nameof(hash1));
            ArgumentNullException.ThrowIfNull(hash2, nameof(hash2));

            if (hash1.Length == 0 && hash2.Length == 0)
            {
                return [];
            }

            int length = Math.Max(hash1.Length, hash2.Length);
            byte[] result = new byte[length];

            for (int i = 0; i < length; i++)
            {
                byte b1 = i < hash1.Length ? hash1[i] : (byte)0;
                byte b2 = i < hash2.Length ? hash2[i] : (byte)0;
                result[i] = (byte)((b1 + b2 + 1) / 2);
            }

            return result;
        }
        /// <summary>
        /// 将数个内容哈希融合为一个哈希，融合结果长度取较长者；
        /// 每字节按加权平均（四舍五入）合并，较短的哈希缺失字节视为 0，
        /// 使得融合哈希同时保留源哈希的内容特征
        /// <br></br>满足幂等率
        /// </summary>
        ///
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public byte[] Merge(IEnumerable<byte[]> hashs)
        {
            // 参数空引用校验：当 hashs 为 null 时抛出 ArgumentNullException，防止后续空引用访问
            ArgumentNullException.ThrowIfNull(hashs, nameof(hashs));
            if (!hashs.Any()) return [];
            byte[] bytes = hashs.First();
            foreach (byte[] hash in hashs)
            {
                bytes = Merge(bytes, hash);
            }
            return bytes;
        }
    }

    /// <summary>
    /// 平均哈希算法
    /// </summary>
    /// <remarks>
    /// 平均哈希算法（Average Hashing）是一种基于像素的哈希算法，
    /// 它通过计算视频帧的像素平均值来生成哈希值。具体来说，
    /// 平均哈希算法首先将视频帧的像素值转换为灰度值，然后计算
    public class AverageHasher : IHasher
    {
        private IHashMerger merger = new AveHashMegerger();


        /// <summary>
        /// 计算两个视频哈希的相似度，1.0 为完全相同，0.0 为完全不同。
        /// 按较短的哈希逐字节比较汉明距离，因此支持任意长度的哈希字节数组
        /// </summary>
        /// <param name="hash1">第一个内容哈希</param>
        /// <param name="hash2">第二个内容哈希</param>
        /// <returns>相似度，取值 [0, 1]</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public double Compare(byte[] hash1, byte[] hash2)
        {
            ArgumentNullException.ThrowIfNull(hash1, nameof(hash1));
            ArgumentNullException.ThrowIfNull(hash2, nameof(hash2));

            // 无公共字节可比较时视为完全不同
            if (hash1.Length == 0 || hash2.Length == 0)
                return 0.0;

            // 按较短哈希的字节数比较，超出部分不参与计算
            int length = Math.Min(hash1.Length, hash2.Length);
            int totalBits = length * 8;
            int diffBits = 0;
            for (int i = 0; i < length; i++)
            {
                // 异或后统计置 1 的位数即为该字节的汉明距离
                diffBits += BitOperations.PopCount((byte)(hash1[i] ^ hash2[i]));
            }
            // 相似度 = 1 - 不同位数占比
            return 1.0 - (double)diffBits / totalBits;
        }
        /// <summary>
        /// 计算哈希值
        /// </summary>
        /// <param name="videoFile"></param>
        /// <param name="sampler"></param>
        /// <param name="samplerArgs"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public virtual byte[] Hash(string videoFile, ISampler sampler, SamplerArgs samplerArgs)
        {
            ArgumentException.ThrowIfNullOrEmpty(videoFile, nameof(videoFile));
            ArgumentNullException.ThrowIfNull(sampler, nameof(sampler));
            ArgumentNullException.ThrowIfNull(samplerArgs, nameof(samplerArgs));

            if (!sampler.CanActHasher(GetType()))
            {
                throw new ArgumentException($"类型为{sampler.GetType().FullName}的采样器不支持此哈希器", nameof(sampler));
            }
            if (!samplerArgs.CanActSample(sampler.GetType()))
            {
                throw new ArgumentException($"类型为{samplerArgs.GetType().FullName}的采样参数不支持类型为{sampler.GetType().FullName}的采样器", nameof(samplerArgs));
            }
            return Hash(sampler.Sample(videoFile, samplerArgs));
        }

        public virtual async Task<byte[]> HashAsync(string videoFile, ISampler sampler, SamplerArgs samplerArgs, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(videoFile, nameof(videoFile));
            ArgumentNullException.ThrowIfNull(sampler, nameof(sampler));
            ArgumentNullException.ThrowIfNull(samplerArgs, nameof(samplerArgs));

            if (!sampler.CanActHasher(GetType()))
            {
                throw new ArgumentException($"类型为{sampler.GetType().FullName}的采样器不支持此哈希器", nameof(sampler));
            }
            if (!samplerArgs.CanActSample(sampler.GetType()))
            {
                throw new ArgumentException($"类型为{samplerArgs.GetType().FullName}的采样参数不支持类型为{sampler.GetType().FullName}的采样器", nameof(samplerArgs));
            }
            return await HashAsync(sampler.SampleAsync(videoFile, samplerArgs, ct), ct);
        }

        /// <summary>
        /// 异步计算样本帧集合的哈希：对每帧解码为图像并计算 AverageHash，再融合为单一哈希
        /// </summary>
        /// <param name="samples">样本帧数据流的异步枚举</param>
        /// <param name="ct">取消令牌，取消时中止帧的获取</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual async Task<byte[]> HashAsync(IAsyncEnumerable<Stream> samples, CancellationToken ct = default)
        {
            // 参数空引用校验
            ArgumentNullException.ThrowIfNull(samples, nameof(samples));

            // 结果集合，存放每帧计算出的感知哈希
            List<byte[]> hashs = [];

            CoenM.ImageHash.HashAlgorithms.AverageHash averageHash = new();
            // 异步逐帧遍历样本流（可被取消），保证每帧流只被读取一次
            await foreach (Stream frame in samples.WithCancellation(ct))
            {
                // 帧获取间隙检查取消，尽早响应取消请求
                ct.ThrowIfCancellationRequested();
                // using 确保图像解码占用的非托管内存及时释放
                // Image.Load<Rgba32> 会将任意像素格式（如 ffmpeg PNG 的 Rgb24）转换为 Rgba32，直接强转会失败
                using Image<Rgba32> img = Image.Load<Rgba32>(frame);
                // 计算单帧感知哈希（ulong），转为大端序字节数组参与后续融合
                hashs.Add(averageHash.Hash(img).ToBytes());
                // 帧流已消费完毕，及时释放
                frame.Dispose();
            }

            // 将全部帧哈希融合为视频内容哈希
            return merger.Merge(hashs);
        }
        /// <summary>
        /// 计算样本帧集合的哈希：对每帧解码为图像并计算 AverageHash，再融合为单一哈希
        /// </summary>
        /// <param name="samples">样本帧数据流集合</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual byte[] Hash(IEnumerable<Stream> samples)
        {
            // 参数空引用校验
            ArgumentNullException.ThrowIfNull(samples, nameof(samples));

            // 结果集合，存放每帧计算出的感知哈希
            List<byte[]> hashs = [];
            CoenM.ImageHash.HashAlgorithms.AverageHash averageHash = new();
            // 逐帧遍历样本流，保证每帧流只被读取一次
            foreach (Stream frame in samples)
            {
                // using 确保图像解码占用的非托管内存及时释放
                // Image.Load<Rgba32> 会将任意像素格式（如 ffmpeg PNG 的 Rgb24）转换为 Rgba32，直接强转会失败
                using Image<Rgba32> img = Image.Load<Rgba32>(frame);
                // 计算单帧感知哈希（ulong），转为大端序字节数组参与后续融合
                hashs.Add(averageHash.Hash(img).ToBytes());
                // 帧流已消费完毕，及时释放
                frame.Dispose();
            }

            // 将全部帧哈希融合为视频内容哈希
            return merger.Merge(hashs);
        }

    }

    public class DiffHasher : IHasher
    {
        private IHashMerger merger = new MajorityVoteMerger();


        /// <summary>
        /// 计算两个视频哈希的相似度，1.0 为完全相同，0.0 为完全不同。
        /// 按较短的哈希逐字节比较汉明距离，因此支持任意长度的哈希字节数组
        /// </summary>
        /// <param name="hash1">第一个内容哈希</param>
        /// <param name="hash2">第二个内容哈希</param>
        /// <returns>相似度，取值 [0, 1]</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public double Compare(byte[] hash1, byte[] hash2)
        {
            ArgumentNullException.ThrowIfNull(hash1, nameof(hash1));
            ArgumentNullException.ThrowIfNull(hash2, nameof(hash2));

            // 无公共字节可比较时视为完全不同
            if (hash1.Length == 0 || hash2.Length == 0)
                return 0.0;

            // 按较短哈希的字节数比较，超出部分不参与计算
            int length = Math.Min(hash1.Length, hash2.Length);
            int totalBits = length * 8;
            int diffBits = 0;
            for (int i = 0; i < length; i++)
            {
                // 异或后统计置 1 的位数即为该字节的汉明距离
                diffBits += BitOperations.PopCount((byte)(hash1[i] ^ hash2[i]));
            }
            // 相似度 = 1 - 不同位数占比
            return 1.0 - (double)diffBits / totalBits;
        }
        /// <summary>
        /// 计算哈希值
        /// </summary>
        /// <param name="videoFile"></param>
        /// <param name="sampler"></param>
        /// <param name="samplerArgs"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public virtual byte[] Hash(string videoFile, ISampler sampler, SamplerArgs samplerArgs)
        {
            ArgumentException.ThrowIfNullOrEmpty(videoFile, nameof(videoFile));
            ArgumentNullException.ThrowIfNull(sampler, nameof(sampler));
            ArgumentNullException.ThrowIfNull(samplerArgs, nameof(samplerArgs));

            if (!sampler.CanActHasher(GetType()))
            {
                throw new ArgumentException($"类型为{sampler.GetType().FullName}的采样器不支持此哈希器", nameof(sampler));
            }
            if (!samplerArgs.CanActSample(sampler.GetType()))
            {
                throw new ArgumentException($"类型为{samplerArgs.GetType().FullName}的采样参数不支持类型为{sampler.GetType().FullName}的采样器", nameof(samplerArgs));
            }
            return Hash(sampler.Sample(videoFile, samplerArgs));
        }

        public virtual async Task<byte[]> HashAsync(string videoFile, ISampler sampler, SamplerArgs samplerArgs, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(videoFile, nameof(videoFile));
            ArgumentNullException.ThrowIfNull(sampler, nameof(sampler));
            ArgumentNullException.ThrowIfNull(samplerArgs, nameof(samplerArgs));

            if (!sampler.CanActHasher(GetType()))
            {
                throw new ArgumentException($"类型为{sampler.GetType().FullName}的采样器不支持此哈希器", nameof(sampler));
            }
            if (!samplerArgs.CanActSample(sampler.GetType()))
            {
                throw new ArgumentException($"类型为{samplerArgs.GetType().FullName}的采样参数不支持类型为{sampler.GetType().FullName}的采样器", nameof(samplerArgs));
            }
            return await HashAsync(sampler.SampleAsync(videoFile, samplerArgs, ct), ct);
        }

        /// <summary>
        /// 异步计算样本帧集合的哈希：对每帧解码为图像并计算 AverageHash，再融合为单一哈希
        /// </summary>
        /// <param name="samples">样本帧数据流的异步枚举</param>
        /// <param name="ct">取消令牌，取消时中止帧的获取</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual async Task<byte[]> HashAsync(IAsyncEnumerable<Stream> samples, CancellationToken ct = default)
        {
            // 参数空引用校验
            ArgumentNullException.ThrowIfNull(samples, nameof(samples));

            // 结果集合，存放每帧计算出的感知哈希
            List<byte[]> hashs = [];

            DifferenceHash diffHash = new();
            // 异步逐帧遍历样本流（可被取消），保证每帧流只被读取一次
            await foreach (Stream frame in samples.WithCancellation(ct))
            {
                // 帧获取间隙检查取消，尽早响应取消请求
                ct.ThrowIfCancellationRequested();
                // using 确保图像解码占用的非托管内存及时释放
                // Image.Load<Rgba32> 会将任意像素格式（如 ffmpeg PNG 的 Rgb24）转换为 Rgba32，直接强转会失败
                using Image<Rgba32> img = Image.Load<Rgba32>(frame);
                // 计算单帧感知哈希（ulong），转为大端序字节数组参与后续融合
                hashs.Add(diffHash.Hash(img).ToBytes());
                // 帧流已消费完毕，及时释放
                frame.Dispose();
            }

            // 将全部帧哈希融合为视频内容哈希
            return merger.Merge(hashs);
        }
        /// <summary>
        /// 计算样本帧集合的哈希：对每帧解码为图像并计算 AverageHash，再融合为单一哈希
        /// </summary>
        /// <param name="samples">样本帧数据流集合</param>
        /// <returns>融合后的内容哈希</returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual byte[] Hash(IEnumerable<Stream> samples)
        {
            // 参数空引用校验
            ArgumentNullException.ThrowIfNull(samples, nameof(samples));

            // 结果集合，存放每帧计算出的感知哈希
            List<byte[]> hashs = [];
            DifferenceHash diffHash = new();
            // 逐帧遍历样本流，保证每帧流只被读取一次
            foreach (Stream frame in samples)
            {
                // using 确保图像解码占用的非托管内存及时释放
                // Image.Load<Rgba32> 会将任意像素格式（如 ffmpeg PNG 的 Rgb24）转换为 Rgba32，直接强转会失败
                using Image<Rgba32> img = Image.Load<Rgba32>(frame);
                // 计算单帧感知哈希（ulong），转为大端序字节数组参与后续融合
                hashs.Add(diffHash.Hash(img).ToBytes());
                // 帧流已消费完毕，及时释放
                frame.Dispose();
            }

            // 将全部帧哈希融合为视频内容哈希
            return merger.Merge(hashs);
        }

    }

    
}
