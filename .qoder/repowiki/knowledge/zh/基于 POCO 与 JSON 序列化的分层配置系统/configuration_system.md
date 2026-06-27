RimLife 采用了一套**基于 POCO（Plain Old CLR Object）与自定义 JSON 序列化**的分层配置系统。该系统不依赖外部配置文件（如 `.json` 或 `.yaml`），而是将配置状态持久化到 RimWorld 的模组设置（Mod Settings）或本地缓存存储（CacheStore）中，实现了从 UI 到核心逻辑的闭环管理。

### 1. 核心架构与分层
配置系统分为三个主要层级：

*   **全局框架配置 (`FrameworkConfig`)**：位于 `NPCLife` 核心库，管理 Agent 驱动参数、诊断开关和功能特性。通过 `RimLifeCore.Config` 访问，持久化于 `CacheStore`（本地文件）。
*   **驱动配置 (`DriverConfig`)**：控制事件触发阈值、定时器脉冲间隔等运行时行为。支持分角色（导演/编剧/即兴）独立配置，持久化于 `CacheStore`。
*   **凭证与连接配置 (`LlmCredential` / `CredentialRegistry`)**：管理 LLM API 的 BaseUrl、ApiKey、ModelName 及提供商类型。通过 `RimLifeModSettings` 持久化到 RimWorld 的全局模组设置中，实现跨存档共享。

### 2. 关键组件与实现

*   **POCO 配置类**：
    *   `FrameworkConfig`：包含 `DriverConfig`、`DiagnosticSection` 和 `FeatureToggleSection`。支持冻结（Freeze）机制，防止运行时意外修改。
    *   `DriverConfig`：定义了各 Agent 角色的事件数量/重要度阈值及定时器间隔。
    *   `LlmConfig` / `LlmCredential`：封装 LLM 访问所需的三元组（URL, Key, Model）及提供商类型。

*   **持久化策略**：
    *   **模组设置持久化**：`RimLifeModSettings` 继承自 `ModSettings`，通过 `ExposeData` 将 `LlmCredentialsJson` 字符串保存到 RimWorld 的全局配置文件中。`CredentialRegistry` 负责将该 JSON 字符串反序列化为内存中的凭证列表。
    *   **缓存存储持久化**：`LocalFileStore` 实现 `ICacheStore` 接口，将 `FrameworkConfig` 和 `DriverConfig` 序列化为 JSON 字符串并存储在本地文件系统（通常为 `Config/Mods` 目录）。

*   **自定义序列化器**：
    *   项目使用了轻量级的 `JsonWriter` 和 `JsonParser`（位于 `NPCLife.Framework`），避免了对重型 JSON 库（如 Newtonsoft.Json）的依赖，确保在 RimWorld 的 .NET 环境下兼容性与性能。

### 3. 配置加载与生效流程

1.  **初始化**：`RimLifeCore` 在首次访问配置属性时，从 `CacheStore` 或 `RimLifeModSettings` 加载 JSON 字符串。
2.  **反序列化**：调用 `FrameworkConfig.FromJson()` 或 `CredentialRegistry.DeserializeState()` 将 JSON 转换为 POCO 对象。
3.  **UI 交互**：`ConnectionPage`（连接配置）和 `NarrativePage`（叙事配置）直接读写内存中的配置对象或编辑缓冲区。
4.  **保存与重建**：用户点击“保存”后，配置被序列化回存储后端。对于驱动配置的修改，需调用 `RimLifeCore.RebuildAgents()` 销毁并重建 Agent 实例以应用新参数。

### 4. 开发者规范

*   **配置不可变性**：`FrameworkConfig` 在应用后应调用 `Freeze()`，后续修改需通过创建新实例并调用 `RimLifeCore.Configure()` 完成。
*   **线程安全**：`CredentialRegistry` 内部使用锁（`_lock`）保护凭证状态的读写，UI 线程在访问凭证列表时应注意异步操作的状态同步。
*   **持久化委托**：`CredentialRegistry` 通过注入 `persistAction` 委托实现与宿主环境（RimWorld Mod Settings）的解耦，新增配置项时需确保其能被正确序列化到 JSON 结构中。