using System;
using System.Reflection;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 统一的 MCP 工具载体。无论来源是反射方法还是外部 Hook 提供者，都收敛到此类型。
    /// Definition 描述工具签名（供 LLM 消费），Invoker 是 jsonArgs → jsonResult 调用委托。
    /// 
    /// 创建方式：
    ///   1. McpTool.FromMethod(method, target) — 从 MethodInfo 包装
    ///   2. new McpTool { Definition = ..., Invoker = ... } — 手工构造（Hook 提供者）
    /// </summary>
    public class McpTool
    {
        /// <summary>工具定义元数据：名称、描述、参数 schema。</summary>
        public McpToolDefinition Definition;

        /// <summary>调用委托：接收 JSON 参数字符串，返回 JSON 结果字符串。</summary>
        public Func<string, string> Invoker;

        /// <summary>
        /// 从 MethodInfo 创建 McpTool。自动从方法签名生成 Definition，
        /// Invoker 委托到 McpToolInvoker。
        /// </summary>
        /// <param name="method">目标方法（静态或实例）。</param>
        /// <param name="target">实例方法的目标对象，静态方法传 null。</param>
        public static McpTool FromMethod(MethodInfo method, object target = null)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            return new McpTool
            {
                Definition = McpToolGenerator.GenerateDefinition(method),
                Invoker = jsonArgs => McpToolInvoker.Invoke(method, target, jsonArgs)
            };
        }
    }
}
