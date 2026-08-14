using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoHashSharp
{
    /// <summary>
    /// 视频内容哈希值, 提取了视频的内容感知特征, 能够用于比对视频内容是否相似
    /// </summary>
    public readonly struct VideoHash
    {
        /// <summary>
        /// 视频的视频轨道哈希值, 每个采样帧对应一个哈希
        /// </summary>
        public IReadOnlyCollection<byte[]> VHashs { get; }
        /// <summary>
        /// 视频的音频轨道哈希值
        /// </summary>
        public IReadOnlyCollection<byte> AudioHash { get; }
        /// <summary>
        /// 生成此视频哈希的完整配置(哈希工厂 + 采样器 + 采样参数), 用于从哈希恢复相同的生成方法
        /// </summary>
        public HashPipeline Pipeline { get; }
        /// <summary>
        /// 生成此视频哈希的哈希工厂(便捷访问 <see cref="Pipeline"/> 中的工厂)
        /// <br>对同一个视频使用相同的工厂生成的哈希应当相同</br>
        /// </summary>
        public IHasherFactory Factory => Pipeline.Factory;

        internal VideoHash(IReadOnlyCollection<byte[]> vHashs, IReadOnlyCollection<byte> audioHash, HashPipeline pipeline)
        {
            VHashs = vHashs;
            AudioHash = audioHash;
            Pipeline = pipeline;
        }

    }


    /// <summary>
    /// 采样器采样时所需参数
    /// </summary>
    public class SamplerArgs : ISerializableConfig
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

        /// <summary>
        /// 导出可序列化配置数据(默认无配置), 具体采样参数子类应重写以导出自身字段
        /// </summary>
        public virtual object? ExportConfig() => null;

        /// <summary>
        /// 从配置数据恢复自身状态(默认无配置)
        /// </summary>
        public virtual void ImportConfig(object? config) { }

    }
    /// <summary>
    /// 采样器,用于从视频媒体数据中采样
    /// </summary>
    public interface ISampler
    {
        /// <summary>
        /// 执行采样,并返回样本帧数据流的迭代器
        /// </summary>
        /// <param name="videoFile">视频媒体文件路径</param>
        /// <returns>样本帧数据流的迭代器</returns>
        IEnumerable<Stream> Sample(string videoFile, SamplerArgs samplerArgs);
        /// <summary>
        /// 异步执行采样,并返回样本帧数据流的异步迭代器
        /// </summary>
        /// <param name="videoFile">视频媒体文件路径</param>
        /// <returns>样本帧数据流的异步迭代器</returns>
        IAsyncEnumerable<Stream> SampleAsync(string videoFile, SamplerArgs samplerArgs);
    }

    /// <summary>
    /// 哈希计算器工厂,用于创建哈希计算器和哈希比较器
    /// </summary>
    public interface IHasherFactory // 不同的哈希计算器可能需要的比较方式不同,这个接口可以将哈希计算器和哈希比较器的搭配与它们自身解耦
    {
        /// <summary>
        /// 获取哈希计算器
        /// </summary>
        /// <returns></returns>
        IHasher GetHasher();
        /// <summary>
        /// 获取哈希比较器
        /// </summary>
        /// <returns></returns>
        IHashComparer GetHashComparer();
    }
    /// <summary>
    /// 哈希计算器,用于计算样本数据的哈希值,以及比对两个哈希值是否相似
    /// </summary>
    public interface IHasher
    {
        /// <summary>
        /// 计算样本数据的哈希值, 为每一个样本生成哈希,并返回哈希值的数组
        /// </summary>
        /// <param name="videoFile">视频媒体文件</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <returns>样本数据的哈希值</returns>
        byte[][] Hash(string videoFile, ISampler sampler, SamplerArgs samplerArgs);
        /// <summary>
        /// 异步计算视频媒体数据流的哈希值
        /// </summary>
        /// <param name="videoFile">视频媒体文件</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <returns>视频媒体数据流的哈希值</returns>
        Task<byte[][]> HashAsync(string videoFile, ISampler sampler, SamplerArgs samplerArgs);
    }
    /// <summary>
    /// 哈希比较器,用于计算两个<see cref="VideoHash"/>的相似度
    /// </summary>
    public interface IHashComparer
    {
        /// <summary>
        /// 计算两个<see cref="VideoHash"/>的相似度
        /// </summary>
        /// <returns>相似度</returns>
        double Compare(VideoHash h1, VideoHash h2);
    }

    /// <summary>
    /// 可序列化配置契约: 使实现类能把自己的关键状态导出为可序列化数据, 并能从该数据恢复自身。
    /// </summary>
    /// <remarks>
    /// <see cref="HashPipeline"/> 需要持久化采样器、采样参数与哈希工厂, 但它们都是接口/抽象类型的引用,
    /// 内部字段各异且可能含不可序列化成员, 无法由 <see cref="HashPipeline"/> 统一序列化。
    /// 因此把「导出/还原自身状态」的职责交给各实现类, <see cref="HashPipeline"/> 只需记录类型并通过本接口搬运数据。
    /// 约定: 实现类必须提供公开无参构造函数, 以便反序列化时先反射创建空实例, 再调用 <see cref="ImportConfig"/> 恢复状态。
    /// </remarks>
    public interface ISerializableConfig
    {
        /// <summary>
        /// 导出可序列化的配置数据: 把重建自身所必需的字段打包成一个可 JSON 序列化的对象
        /// </summary>
        /// <returns>配置数据(原语或简单 DTO); 无状态时返回 null</returns>
        object? ExportConfig();

        /// <summary>
        /// 从配置数据恢复自身状态: 在反射创建的空实例上, 把 <see cref="ExportConfig"/> 导出的数据重新注入
        /// </summary>
        /// <param name="config">
        /// 由 <see cref="ExportConfig"/> 导出的反序列化结果, 实际类型为 Newtonsoft.Json.Linq.JToken 或 null;
        /// 实现类需自行将其转换为自己的配置类型
        /// </param>
        void ImportConfig(object? config);
    }
}

