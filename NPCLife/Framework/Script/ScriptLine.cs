namespace NPCLife.Framework.Script
{
    /// <summary>
    /// 台词行的语义类型。
    /// </summary>
    public enum ScriptLineType
    {
        /// <summary>角色对话。</summary>
        Dialogue,

        /// <summary>旁白/环境描写。</summary>
        Narration,

        /// <summary>动作描写（无声的肢体动作/事件）。</summary>
        Action,

        /// <summary>纯停顿（无文本，仅延迟）。</summary>
        Pause
    }

    /// <summary>
    /// 一句台词的完整数据契约。格式模块产出此结构，游戏侧消费此结构。
    /// SpeakerName 由 IScriptLineResolver 填充，其余字段由 ScriptFormat.Parse 填充。
    /// </summary>
    public class ScriptLine
    {
        /// <summary>说话者的 pawn ThingID。Dialogue 类型必填，其余可为 null。</summary>
        public string SpeakerId;

        /// <summary>解析后的显示名。由 IScriptLineResolver 在推送前填充。</summary>
        public string SpeakerName;

        /// <summary>台词/描述文本。Pause 类型可为空。</summary>
        public string Text;

        /// <summary>此行语义类型。</summary>
        public ScriptLineType Type;

        /// <summary>相对时间戳（秒），从本批台词起点起算。游戏侧按 Tick 换算。</summary>
        public float RelativeTime;
    }
}
