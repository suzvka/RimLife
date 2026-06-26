using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 本 Mod 专有的日志条目类型：记录角色"听见"了某句对话。
    /// 当一轮台词中的 Dialogue 行被消费时，为所有参与角色（除说话者外）生成此条目。
    /// </summary>
    /// <remarks>
    /// 与说话者的 LogEntry_Dialogue 条目配对出现，确保所有参与者都能看到同一轮对话的上下文。
    /// </remarks>
    public class LogEntry_HeardDialogue : LogEntry
    {
        private Pawn listener;
        private Pawn speaker;
        private string text;

        public LogEntry_HeardDialogue() : base(null)
        {
            // Scribe 反序列化需要无参构造
        }

        public LogEntry_HeardDialogue(Pawn listener, Pawn speaker, string text) : base(null)
        {
            this.listener = listener;
            this.speaker = speaker;
            this.text = text;
        }

        // RimWorld API: 返回此条目涉及的所有 Thing
        public override IEnumerable<Thing> GetConcerns()
        {
            if (listener != null) yield return listener;
        }

        // RimWorld API: 判断此条目是否涉及指定的 Thing
        public override bool Concerns(Thing thing)
        {
            return thing == listener;
        }

        // RimWorld API: 生成显示文本
        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
        {
            string speakerName = speaker?.LabelShortCap ?? "?";
            return $"听见. {speakerName}: {text}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref listener, "listener");
            Scribe_References.Look(ref speaker, "speaker");
            Scribe_Values.Look(ref text, "text");
        }
    }
}
