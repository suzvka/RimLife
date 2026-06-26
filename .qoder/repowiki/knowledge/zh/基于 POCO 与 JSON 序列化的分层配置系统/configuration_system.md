RimLife AI 叙事模组采用了一套**分层、持久化且支持运行时热重载**的配置系统。该系统将全局框架行为、LLM 连接凭证、Agent 驱动参数以及提示词模板进行了逻辑隔离，并通过 RimWorld 的 `ModSettings` 机制与本地文件系统（JSON）实现持久化。

### 1. 核心架构与分层
配置系统分为三个主要层级：

*   **UI/交互层 (RimLife.UI)**: 提供多页签配置界面（`ConfigPanelLayout`），包括连接管理 (`ConnectionPage`)、高级功能开关 (`AdvancedPage`) 等。UI 组件不直接持有状态，而是通过服务定位器 (`RimLifeCore`) 读写底层配置。
*   **核心管理层 (RimLife.Infrastructure)**: `RimLifeCore` 作为服务定位器，统一管理 `FrameworkConfig`、`DriverConfig`、`PromptConfig` 和 `CredentialRegistry`。它负责配置的加载、验证、冻结（Freeze）以及向子系统分发。
*   **数据定义层 (NPCLife.Framework/Driver)**: 定义纯 POCO 配置类，如 `FrameworkConfig`（功能开关/诊断）、`DriverConfig`（事件阈值/定时器）、`LlmConfig`（API 三元组）等。这些类零外部依赖，支持 JSON 序列化。

### 2. 关键配置模块

#### A. LLM 凭证管理 (CredentialRegistry)
*   **存储方式**: 凭证信息（BaseUrl, ApiKey, ModelName, ProviderType）以 JSON 字符串形式存储在 `RimLifeModSettings.LlmCredentialsJson` 中，利用 RimWorld 的 `Scribe_Values` 机制持久化到全局配置文件（`ModsConfig.xml`），**不绑定特定存档**。
*   **管理逻辑**: `CredentialRegistry` 维护一个凭证字典和激活顺序列表（Fallback 链路）。支持动态增删改查、模型发现（ListModels）及连接测试。
*   **安全实践**: UI 层提供 API Key 的显示/隐藏切换，并在内存中通过 `LlmCredential` 对象管理，避免明文日志输出。

#### B. 框架功能与诊断 (FrameworkConfig)
*   **功能开关**: 控制导演 Agent、记忆巩固、知识库、Freelancer Agent 及运行时度量的启用状态。
*   **诊断配置**: 支持详细日志、工具调用追踪和事件总线追踪的动态开启。
*   **不可变性**: 配置应用后调用 `Freeze()` 方法，防止运行时意外修改，确保系统稳定性。

#### C. Agent 驱动与提示词 (DriverConfig & PromptConfig)
*   **持久化**: 这两类配置通过 `LocalFileStore` (ICacheStore) 持久化为本地 JSON 文件（`rimlife_driver_config.json`, `rimlife_prompt_config.json`），位于游戏本地缓存目录。
*   **热重载**: 修改后需调用 `RimLifeCore.RebuildAgents()` 销毁并重建 Agent 实例，使新参数（如提示词内容、事件触发阈值）生效。

### 3. 持久化策略对比

| 配置类型 | 存储后端 | 作用域 | 关键类/字段 |
| :--- | :--- | :--- | :--- |
| **LLM 凭证** | RimWorld Mod Settings | 全局 (跨存档) | `RimLifeModSettings.LlmCredentialsJson` |
| **驱动参数** | 本地 JSON 文件 | 全局 (跨存档) | `LocalFileStore` -> `rimlife_driver_config` |
| **提示词模板** | 本地 JSON 文件 | 全局 (跨存档) | `LocalFileStore` -> `rimlife_prompt_config` |
| **功能开关** | 内存 (运行时) | 当前会话 | `RimLifeCore.Configure()` |

### 4. 开发者规范
1.  **配置访问**: 严禁直接实例化配置类，应通过 `RimLifeCore.Config`、`RimLifeCore.DriverConfig` 等静态属性访问。
2.  **状态同步**: 修改 `DriverConfig` 或 `PromptConfig` 后，必须调用 `RimLifeCore.SetDriverConfig()` 或 `SetPromptConfig()` 以触发持久化和 Agent 重建。
3.  **线程安全**: 凭证注册表和配置加载过程包含锁机制（`lock`），在多线程环境下（如异步 LLM 请求）访问配置时需确保使用线程安全的接口。
4.  **扩展配置**: 新增配置项应在 `NPCLife.Framework` 中定义 POCO 类，并在 `FrameworkConfig.ToJson/FromJson` 中实现序列化逻辑，同时在 `AdvancedPage` 中添加对应的 UI 控件。