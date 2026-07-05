using NPCLife.Framework;
using NPCLife.Framework.Llm;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 第二轮及之后轮次注入补救提示词，防止 LLM 在中间轮次以纯文本结束对话。
    /// 在 Agent 循环中 Round >= 1 时，向 LLM 请求消息末尾追加一条 user 消息，
    /// 警告 LLM 不应开启新对话，必须用工具调用完成所有任务。
    /// </summary>
    public class RoundTwoWarningInterceptor : AgentInterceptorBase
    {
        private const string WarningMessage =
            "你新开了一轮对话！这会导致大量扣分，你必须慎重考虑查更多信息。建议立即在下一轮中完成所有工作，并确保最后一个工具是结束工具。";

        public override void OnBeforeLlm(LlmContext ctx)
        {
            if (ctx.Round >= 1 && ctx.Request?.Messages != null)
            {
                ctx.Request.Messages.Add(LlmMessage.User(WarningMessage));
            }
        }
    }
}
