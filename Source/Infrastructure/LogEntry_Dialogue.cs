using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 本 Mod 专有的日志条目类型：记录角色自己"说"了某句对话。
    /// 仅出现在说话者自己的日志页中。
    /// </summary>
    public class LogEntry_Dialogue : LogEntry
    {
        private Pawn speaker;
        private string text;

        public LogEntry_Dialogue() : base(null)
        {
            // Scribe 反序列化需要无参构造
        }

        public LogEntry_Dialogue(Pawn speaker, string text) : base(null)
        {
            this.speaker = speaker;
            this.text = text;
        }

        // RimWorld API: 返回此条目涉及的所有 Thing
        public override IEnumerable<Thing> GetConcerns()
        {
            if (speaker != null) yield return speaker;
        }

        // RimWorld API: 判断此条目是否涉及指定的 Thing
        public override bool Concerns(Thing thing)
        {
            return thing == speaker;
        }

        // RimWorld API: 生成显示文本
        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
        {
            return $"说: {text}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref speaker, "speaker");
            Scribe_Values.Look(ref text, "text");
        }
    }
}
