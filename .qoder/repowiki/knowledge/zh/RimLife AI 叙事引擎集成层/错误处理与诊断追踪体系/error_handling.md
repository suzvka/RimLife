该仓库采用基于静态全局钩子（Static Global Hooks）和事件总线（EventBus）的混合错误处理架构，旨在实现框架核心逻辑与宿主环境（游戏引擎）的解耦。

### 1. 核心组件与模式
*   **ErrorHandler (全局中枢)**：位于 `ext/NPCLife/src/NPCLife/Framework/ErrorHandler.cs`。提供纯静态的错误报告接口 `ReportError`。它不直接抛出异常，而是将错误封装为 `ErrorContext` 并分发给注册的处理器。支持**诊断模式**（DiagnosticMode）以输出详细日志。
*   **请求链路追踪 (Trace ID)**：通过 `BeginTrace()` 和 `EndTrace()` 管理线程局部的 Trace ID。在 Agent 循环开始时开启，结束时关闭，确保所有关联的错误和日志都能追溯到具体的执行批次（Run ID）。
*   **EventBus (异步通知)**：定义了一系列预置事件（如 `llm.error`, `agent.loop_finished`）。错误发生时，除了调用 `ErrorHandler`，还会通过 `EventBus.Publish` 发送事件，允许外部系统以松耦合方式监听故障。
*   **AgentPipeline (拦截器)**：在 `AgentPipeline.cs` 中实现了类似中间件的拦截机制。拦截器中的异常会被捕获并记录警告，防止单个拦截器的故障导致整个 Agent 循环崩溃。

### 2. 错误传播与恢复策略
*   **AgentLoop 容错**：在 `AgentLoop.cs` 的主循环中，采用 `try-catch` 包裹整个执行流程。
    *   **取消处理**：捕获 `OperationCanceledException` 并标记为取消。
    *   **通用故障**：捕获其他 `Exception`，调用 `FailAndRequeue`。该策略会将当前已处理的事件**回灌（Requeue）**到事件池中，以便后续重试，而不是直接丢弃。
*   **MCP 工具调用隔离**：在 `McpSkillRegistry.cs` 中，工具执行被包裹在 `try-catch` 中。如果工具抛出异常，会返回一个 JSON 格式的错误字符串 `{"error": "..."}` 给 LLM，而不是让框架崩溃。这遵循了“将错误作为上下文反馈给智能体”的设计哲学。
*   **异常隔离**：`ErrorHandler` 和 `EventBus` 在遍历处理器列表时，会对每个处理器的执行进行独立的 `try-catch` 保护，确保一个错误的观察者不会阻止其他观察者的执行。

### 3. 开发者规范
*   **禁止静默吞掉异常**：在底层基础设施（如 LLM 适配器、存储层）中，应抛出标准 .NET 异常或调用 `ErrorHandler.ReportError`。
*   **使用 Trace ID**：在进行跨模块调试时，应利用 `ErrorHandler.CurrentTraceId` 关联日志。
*   **工具开发准则**：编写 MCP 工具时，建议内部处理异常并返回结构化错误信息，由框架统一通过 `ErrorHandler` 上报严重故障。
*   **注册全局处理器**：宿主程序应在初始化阶段通过 `ErrorHandler.OnError` 注册统一的错误上报逻辑（如写入游戏日志文件或触发 UI 提示）。