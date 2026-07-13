using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 本 Mod 专有的日志条目类型：记录角色自己"说"了某句对话。
    /// 继承 PlayLogEntry_Interaction 以被 Bubbles 模组识别为闲聊（Chat）交互类型。
    /// 仅出现在说话者自己的日志页中。
    /// </summary>
    public class LogEntry_Dialogue : PlayLogEntry_Interaction
    {
        private string text;

        /// <summary>
        /// 延迟解析 Chat InteractionDef。
        /// 尝试 defName="Chat"，若不存在则从所有 InteractionDef 中按关键字匹配。
        /// </summary>
        private static InteractionDef ResolveChatDef()
        {
            // RimWorld 闲聊交互的 defName 是 "Chitchat"（Interactions_Social.xml）
            return DefDatabase<InteractionDef>.GetNamedSilentFail("Chitchat")
                ?? DefDatabase<InteractionDef>.GetNamedSilentFail("Chat")
                ?? DefDatabase<InteractionDef>.AllDefs.FirstOrDefault();
        }

        private static InteractionDef ChatDef => ResolveChatDef();

        public LogEntry_Dialogue() : base()
        {
            // Scribe 反序列化需要无参构造
        }

        public LogEntry_Dialogue(Pawn speaker, string text)
            : base(ResolveChatDef(), speaker, speaker, null)
        {
            this.text = text;
        }

        // RimWorld API: 生成显示文本（Bubbles 模组通过此方法获取气泡内容）
        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
        {
            return $"说: {text}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref text, "text");
        }
    }
}
