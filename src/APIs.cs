using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoHashSharp
{
    public readonly struct VideoHash
    {
        /// <summary>
        /// 视频媒体数据的内容哈希值
        /// </summary>
        public byte[] Hash { get; }
        /// <summary>
        /// 构建此哈希值所需的采样参数
        /// </summary>
        public SamplerArgs BuildArgs { get; }
        /// <summary>
        /// 构建此哈希值使用的哈希器的类型
        /// </summary>
        public Type HasherType { get; }

        public VideoHash(byte[] hash, SamplerArgs buildArgs, Type hasherType)
        {
            Hash = hash;
            BuildArgs = buildArgs;
            HasherType = hasherType;
        }

        public override string ToString()
        {
            return BitConverter.ToString(Hash);
        }
    }


    /// <summary>
    /// 采样器采样时所需参数
    /// </summary>
    public class SamplerArgs
    {
        /// <summary>
        /// 此采样参数是否可以作用于指定类型的采样器
        /// </summary>
        /// <param name="samplerType">被测试的采样器类型</param>
        /// <returns></returns>
        public virtual bool CanActSample(Type samplerType)
        {
            return samplerType.IsAssignableTo(typeof(ISampler));
        }

    }
    /// <summary>
    /// 采样器,用于从视频媒体数据流中采样
    /// </summary>
    public interface ISampler
    {
        /// <summary>
        /// 执行采样,并返回样本帧数据流的迭代器
        /// </summary>
        /// <param name="videoStream">视频媒体数据流</param>
        /// <returns>样本帧数据流的迭代器</returns>
        IEnumerable<Stream> Sample(Stream videoStream);
        /// <summary>
        /// 异步执行采样,并返回样本帧数据流的异步迭代器
        /// </summary>
        /// <param name="videoStream">视频媒体数据流</param>
        /// <returns>样本帧数据流的异步迭代器</returns>
        IAsyncEnumerable<Stream> SampleAsync(Stream videoStream);
        /// <summary>
        /// 返回当前采样器是否可以作用于指定类型的哈希器
        /// </summary>
        /// <param name="hasherType">被测试的哈希器类型</param>
        /// <returns></returns>
        bool CanActHasher(Type hasherType);

    }
    /// <summary>
    /// 哈希器,用于计算样本帧数据流的哈希值,以及比对两个哈希值是否相似
    /// </summary>
    public interface IHasher
    {
        /// <summary>
        /// 计算视频媒体数据流的哈希值
        /// </summary>
        /// <param name="videoStream">视频媒体数据流</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <returns>视频媒体数据流的哈希值</returns>
        byte[] Hash(Stream videoStream, ISampler sampler, SamplerArgs samplerArgs);
        /// <summary>
        /// 计算两个视频哈希相似度,1.0为完全相同,0.0为完全不同
        /// </summary>
        /// <param name="hash1"></param>
        /// <param name="hash2"></param>
        /// <returns></returns>
        double Compare(byte[] hash1, byte[] hash2);
    }
}

