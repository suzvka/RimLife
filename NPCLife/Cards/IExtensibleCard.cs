using System.Collections.Generic;

namespace NPCLife.Cards
{
    /// <summary>
    /// 可扩展卡片接口：允许使用者在卡片 DTO 上挂载自定义字段，
    /// 序列化时这些字段会被平铺到顶层 JSON 中。
    /// </summary>
    public interface IExtensibleCard
    {
        /// <summary>扩展字段，key-value 会被序列化到卡片 JSON 的顶层。</summary>
        Dictionary<string, string> ExtensionFields { get; }
    }
}
