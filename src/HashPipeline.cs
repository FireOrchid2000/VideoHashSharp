using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VideoHashSharp
{
    /// <summary>
    /// 哈希生成配置: 聚合哈希工厂、采样器与采样参数, 作为「从哈希恢复生成方法」的完整载体。
    /// </summary>
    /// <remarks>
    /// 计算一个视频哈希需要三样东西: 用什么算法(哈希工厂)、怎么采样(采样器)、按什么参数采样(采样参数)。
    /// 单独一个 <see cref="IHasherFactory"/> 只能恢复算法, 无法恢复采样方式; 因此把它们三者聚合在本对象中,
    /// 使 <see cref="VideoHash"/> 能完整记录并重现其生成过程。
    /// 本对象还支持 <see cref="ToJson"/>/<see cref="FromJson"/> 序列化持久化, 以便哈希跨进程或落盘后仍能还原生成方法。
    /// </remarks>
    public sealed class HashPipeline
    {
        /// <summary>
        /// 哈希工厂, 决定哈希算法与比较方式
        /// </summary>
        public IHasherFactory Factory { get; }

        /// <summary>
        /// 采样器, 决定如何从视频中采样
        /// </summary>
        public ISampler Sampler { get; }

        /// <summary>
        /// 采样参数
        /// </summary>
        public SamplerArgs SamplerArgs { get; }

        /// <summary>
        /// 聚合给定的工厂、采样器与采样参数, 构造一个哈希生成配置
        /// </summary>
        /// <param name="factory">哈希工厂</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <exception cref="ArgumentNullException">任意参数为 null 时抛出</exception>
        public HashPipeline(IHasherFactory factory, ISampler sampler, SamplerArgs samplerArgs)
        {
            // 三个成员都是「恢复生成方法」所必需的, 任一为 null 都会导致配置不完整, 直接拒绝
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            SamplerArgs = samplerArgs ?? throw new ArgumentNullException(nameof(samplerArgs));
        }

        /// <summary>
        /// 将当前配置序列化为 JSON 字符串
        /// </summary>
        /// <returns>配置的 JSON 表示(带缩进, 便于阅读)</returns>
        /// <remarks>
        /// 每个组件被序列化为「类型 + 配置数据」的描述, 大致结构如下:
        /// <code>
        /// {
        ///   "Version": 1,
        ///   "Factory":     { "Type": "...", "Config": null },
        ///   "Sampler":     { "Type": "...", "Config": null },
        ///   "SamplerArgs": { "Type": "...", "Config": { ... } }
        /// }
        /// </code>
        /// </remarks>
        public string ToJson()
        {
            // 把三个成员分别转成可序列化的 DTO 描述(类型 + 配置), 再整体序列化
            var dto = new PipelineDto
            {
                Version = CurrentVersion,
                Factory = ComponentDto.From(Factory),
                Sampler = ComponentDto.From(Sampler),
                SamplerArgs = ComponentDto.From(SamplerArgs),
            };
            return JsonConvert.SerializeObject(dto, Formatting.Indented);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化, 按记录的类型重建各组件并注入配置
        /// </summary>
        /// <param name="json">由 <see cref="ToJson"/> 生成的 JSON 字符串</param>
        /// <returns>重建后的哈希生成配置</returns>
        /// <exception cref="FormatException">JSON 无法解析, 或缺少某组件的配置</exception>
        /// <exception cref="NotSupportedException">配置版本不受支持</exception>
        /// <exception cref="TypeLoadException">记录的类型无法加载</exception>
        /// <exception cref="InvalidOperationException">类型无法用公开无参构造函数重建</exception>
        public static HashPipeline FromJson(string json)
        {
            // 反序列化为 DTO; 结果为空(或 JSON 无效)时报错
            var dto = JsonConvert.DeserializeObject<PipelineDto>(json)
                ?? throw new FormatException("无法解析哈希生成配置 JSON");

            // 校验版本号, 防止读取到不同版本格式导致字段错位
            if (dto.Version != CurrentVersion)
                throw new NotSupportedException($"不支持的配置版本: {dto.Version}");

            // 依次重建三个组件: 每个组件先按类型反射重建实例, 再注入其配置数据
            return new HashPipeline(
                (IHasherFactory)(dto.Factory ?? throw new FormatException("缺少哈希工厂配置")).Create(),
                (ISampler)(dto.Sampler ?? throw new FormatException("缺少采样器配置")).Create(),
                (SamplerArgs)(dto.SamplerArgs ?? throw new FormatException("缺少采样参数配置")).Create());
        }

        // 当前序列化格式的版本号; 后续若修改格式需递增, 并在 FromJson 中做兼容处理
        private const int CurrentVersion = 1;
    }

    /// <summary>
    /// <see cref="IHasherFactory"/> 的便捷扩展方法: 使用工厂、采样器与采样参数一键计算视频哈希。
    /// </summary>
    public static class HasherFactoryExtensions
    {
        /// <summary>
        /// 计算视频哈希, 并携带完整的生成配置
        /// </summary>
        /// <param name="factory">哈希工厂</param>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <returns>包含视频哈希与完整生成配置的 <see cref="VideoHash"/></returns>
        /// <exception cref="ArgumentNullException">任意参数为 null 时抛出</exception>
        /// <remarks>音频轨道哈希暂未实现, 返回值中 <see cref="VideoHash.AudioHash"/> 为空集合。</remarks>
        public static VideoHash Compute(this IHasherFactory factory, string videoFile, ISampler sampler, SamplerArgs samplerArgs)
        {
            // 参数校验, 避免 null 传播到后续计算
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(sampler);
            ArgumentNullException.ThrowIfNull(samplerArgs);

            // 先聚合生成配置, 确保算出的哈希一定携带可恢复的生成方法
            var pipeline = new HashPipeline(factory, sampler, samplerArgs);

            // 计算视频轨道逐帧哈希
            byte[][] vHashs = factory.GetHasher().Hash(videoFile, sampler, samplerArgs);

            // 音频轨道哈希暂未实现, 先以空集合占位
            IReadOnlyCollection<byte> audioHash = [];

            // 组装最终结果: 视频哈希 + 音频哈希 + 生成配置
            return new VideoHash(vHashs, audioHash, pipeline);
        }

        /// <summary>
        /// 异步计算视频哈希, 并携带完整的生成配置
        /// </summary>
        /// <param name="factory">哈希工厂</param>
        /// <param name="videoFile">视频文件路径</param>
        /// <param name="sampler">采样器</param>
        /// <param name="samplerArgs">采样参数</param>
        /// <returns>包含视频哈希与完整生成配置的 <see cref="VideoHash"/></returns>
        /// <exception cref="ArgumentNullException">任意参数为 null 时抛出</exception>
        /// <remarks>音频轨道哈希暂未实现, 返回值中 <see cref="VideoHash.AudioHash"/> 为空集合。</remarks>
        public static async Task<VideoHash> ComputeAsync(this IHasherFactory factory, string videoFile, ISampler sampler, SamplerArgs samplerArgs)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(sampler);
            ArgumentNullException.ThrowIfNull(samplerArgs);

            var pipeline = new HashPipeline(factory, sampler, samplerArgs);

            // 异步计算视频轨道逐帧哈希; ConfigureAwait(false) 避免在无同步上下文的环境中等待时死锁
            byte[][] vHashs = await factory.GetHasher().HashAsync(videoFile, sampler, samplerArgs).ConfigureAwait(false);

            // 音频轨道哈希暂未实现, 先以空集合占位
            IReadOnlyCollection<byte> audioHash = [];

            return new VideoHash(vHashs, audioHash, pipeline);
        }
    }

    /// <summary>
    /// 生成配置的内部序列化 DTO, 仅用于 JSON 编解码, 不对外暴露
    /// </summary>
    internal sealed class PipelineDto
    {
        /// <summary>序列化格式版本号</summary>
        public int Version { get; set; }

        /// <summary>哈希工厂组件描述</summary>
        public ComponentDto? Factory { get; set; }

        /// <summary>采样器组件描述</summary>
        public ComponentDto? Sampler { get; set; }

        /// <summary>采样参数组件描述</summary>
        public ComponentDto? SamplerArgs { get; set; }
    }

    /// <summary>
    /// 单个组件(工厂/采样器/采样参数)的序列化描述: 由「类型限定名 + 配置数据」两部分组成
    /// </summary>
    /// <remarks>
    /// 组件是接口/抽象类型的引用, 直接序列化会把内部实现细节一并导出, 脆弱且不可控。
    /// 因此这里只记录两样「重建所需」的最小信息: 具体类型(用于反射重建)与配置数据(用于恢复状态)。
    /// </remarks>
    internal sealed class ComponentDto
    {
        /// <summary>组件具体类型的程序集限定名</summary>
        public string? Type { get; set; }

        /// <summary>组件导出的配置数据(无配置时为 null)</summary>
        public JToken? Config { get; set; }

        /// <summary>
        /// 将任意组件实例转为其序列化描述
        /// </summary>
        /// <param name="component">待序列化的组件(工厂/采样器/采样参数)</param>
        /// <returns>组件的「类型 + 配置」描述</returns>
        public static ComponentDto From(object component)
        {
            // 记录组件的具体运行时类型(含程序集限定名), 反序列化时据此反射重建
            var dto = new ComponentDto { Type = component.GetType().AssemblyQualifiedName };

            // 仅当组件实现了 ISerializableConfig 且导出非空配置时, 才保存配置数据;
            // 未实现或无配置的组件视为无状态, 靠公开无参构造即可重建
            if (component is ISerializableConfig sc && sc.ExportConfig() is { } config)
                dto.Config = JToken.FromObject(config);

            return dto;
        }

        /// <summary>
        /// 按记录的类型重建组件实例, 并注入配置数据恢复其状态
        /// </summary>
        /// <returns>重建后的组件实例</returns>
        /// <exception cref="TypeLoadException">记录的类型无法加载</exception>
        /// <exception cref="InvalidOperationException">类型没有公开无参构造函数</exception>
        public object Create()
        {
            // 用限定名解析出类型; throwOnError 为 true 时类型不存在会立即抛出 TypeLoadException
            Type type = System.Type.GetType(Type!, throwOnError: true)
                ?? throw new TypeLoadException($"无法加载类型: {Type}");

            object instance;
            try
            {
                // 反射调用公开无参构造函数创建实例; 实现类必须提供它(见 ISerializableConfig 约定)
                instance = Activator.CreateInstance(type)!;
            }
            catch (MissingMethodException ex)
            {
                // 无无参构造时转成更易读的错误信息
                throw new InvalidOperationException($"无法创建类型 {Type} 的实例, 实现类需提供公开无参构造函数", ex);
            }

            // 组件若实现了 ISerializableConfig, 把反序列化得到的配置注入, 恢复其运行时状态
            if (instance is ISerializableConfig sc)
                sc.ImportConfig(Config);

            return instance;
        }
    }
}
