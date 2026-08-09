using System;
using System.Collections.Generic;
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

        public bool CanActHasher(Type hasherType)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Stream> Sample(string videoFile, SamplerArgs samplerArgs)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<Stream> SampleAsync(string videoFile, SamplerArgs samplerArgs)
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
