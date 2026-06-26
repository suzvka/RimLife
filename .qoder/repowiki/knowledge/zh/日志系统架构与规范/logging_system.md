## 1. 核心架构：接口隔离与适配器模式

RimLife 采用**接口隔离（Interface Segregation）**与**适配器（Adapter）**模式构建其日志系统，实现了底层框架（NPCLife）与宿主环境（RimWorld）的解耦。

*   **统一接口 (`ILogger`)**: 位于 `ext/NPCLife/src/NPCLife/Framework/ILogger.cs`。定义了 `Message`、`Warning`、`Error` 三个基础方法。所有核心组件（如 AgentLoop, ErrorHandler, EventBus）仅依赖此接口，不直接引用任何具体日志实现。
*   **服务注入**: 通过 `RimLifeCore.InitializeAdapter()` 在启动时将具体的 Logger 实例注入到全局静态字段中，供框架内部使用。

## 2. 关键实现类

### 2.1 适配器实现
*   **`RimWorldLogger`**: 将 `ILogger` 调用桥接到 RimWorld 原生的 `Verse.Log`。用于输出到游戏控制台和 `Player.log` 文件。
*   **`UiLoggerAdapter`**: 将 `ILogger` 调用重定向到 UI 层的 `LogBuffer`。用于在游戏内的调试窗口实时显示日志。

### 2.2 UI 日志缓冲 (`LogBuffer`)
位于 `Source/UI/LogBuffer.cs`，是一个线程安全的静态类，负责管理 UI 端的日志显示：
*   **双存储结构**: 
    *   `_entries`: 结构化列表，保留时间戳、消息内容和类型，支持导出。
    *   `_textBuffer`: `StringBuilder`，维护带有 Rich Text 格式（颜色标签）的增量文本，用于高效渲染。
*   **容量限制**: 默认保留最近 500 条日志，超出时自动移除最旧条目并重建文本缓冲区。
*   **富文本支持**: 根据日志级别自动添加颜色标签（Info: 灰色, Warning: 黄色, Error: 红色）。

### 2.3 错误处理集成 (`ErrorHandler`)
位于 `ext/NPCLife/src/NPCLife/Framework/ErrorHandler.cs`：
*   **诊断模式**: 通过 `DiagnosticMode` 开关控制详细日志的输出。启用后，错误报告会包含 TraceId 等上下文信息。
*   **链路追踪**: 支持 `BeginTrace` / `EndTrace`，为异步或长周期的 Agent 操作生成唯一的 TraceId，便于在日志中追踪请求全生命周期。

## 3. 日志级别与规范

| 级别 | 方法 | 用途 | 示例 |
| :--- | :--- | :--- | :--- |
| **Message** | `Message()` | 常规信息、状态变更、初始化完成 | "Configuration applied", "Agent created" |
| **Warning** | `Warning()` | 非致命错误、配置校验失败、降级处理 | "Config validation failed", "Hook provider failed" |
| **Error** | `Error()` | 致命错误、异常捕获、功能失效 | "FlushToAuthorityStore failed" |

## 4. 开发者指南

1.  **依赖注入**: 在编写 NPCLife 框架层代码时，严禁直接使用 `Console.WriteLine` 或 `Verse.Log`。必须通过构造函数或静态属性获取 `ILogger` 实例。
2.  **UI 日志写入**: 若需在 RimLife 适配层直接向 UI 窗口写入日志，应使用 `RimLifeLogger.Info/Warning/Error` 静态方法，它们会自动处理线程安全和格式化。
3.  **异常处理**: 在捕获异常时，建议使用 `ErrorHandler.ReportError(source, ex)` 而非直接记录日志。这样可以触发全局错误钩子，并自动关联当前的 TraceId。
4.  **性能考量**: `LogBuffer` 在达到容量上限时会执行 `RebuildTextBuffer`（O(N) 复杂度）。虽然 500 条的限制已做优化，但在高频循环中应避免产生大量日志。
