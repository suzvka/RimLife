## 项目概述

RimLife 是一个 RimWorld 游戏模组（Mod），作为 **NPCLife** 框架的游戏侧适配层，负责：
- 通过 Harmony 补丁拦截 RimWorld 游戏事件（袭击、社交互动、死亡等）
- 将原始游戏数据映射为 NPCLife 框架的标准 Card 结构
- 提供 RimWorld 特定的 MCP 工具（角色查询、殖民地概览、记忆系统等）
- 管理配置面板 UI 和 Mod 设置
- 定义各 Agent 角色的系统提示词

Agent 循环、LLM 通信、工作空间管理、MCP 协议、事件总线等框架机制均由 **NPCLife** 提供。

## 技术栈

- 语言: C#
- 运行时: .NET Framework 4.8 (RimWorld 1.6 / Unity)
- 构建: MSBuild (`RimLife.csproj`, `RimLife.sln`)
- 测试: xUnit (`RimLife.Tests/`)
- 框架依赖: **NPCLife** (Git submodule `ext/NPCLife`，通过 ProjectReference 源码引用)
- Harmony: 运行时 IL 注入，拦截游戏方法
- UI 框架: RimWorld IMGUI (Verse.Widgets / Listing_Standard)
- LLM 集成: 由 NPCLife 的 LlmAccessor 提供 (OpenAI 兼容 / Anthropic API)

## 目录结构

```
Source/
  Data/           - RimWorld 数据提取
    PawnPro/      - 角色数据（健康/心情/技能/需求/背景/社交/人格/视角/记忆）
    EnvironmentPro/ - 环境数据
    Colony/       - 殖民地快照
    Events/       - Harmony 事件钩子（EventHooks.cs）
  Infrastructure/ - 桥接与适配
    RimLifeCore.cs       - 核心服务入口，组装 NPCLife 组件
    RimLifeHarmony.cs    - Mod 初始化（注册 ContentProvider、启动 Harmony patch）
    RimWorldAgentDriver.cs - 每帧驱动（定时器脉冲 + 主线程回调）
    RimWorldSaveStore.cs - 存档读写适配
    RimWorldLogger.cs    - 日志适配
    PromptAdditions.cs   - RimLife 侧附加指令与 LLM 参数（基础身份由 NPCLife 持有，此处仅追加）
    Mcp/                 - RimWorld 特定 MCP 工具（角色/殖民地/关系/环境/记忆查询）
    Knowledge/           - 游戏 Def 知识库
  Mappers/        - RimWorld 对象 → NPCLife Card 映射
    EventCardMapper.cs   - 事件卡映射（Incident/Death/MentalBreak/Social/Quest/Letter）
    ColonyContextMapper.cs - 殖民地上下文映射
    EnvironmentCardMapper.cs - 环境卡映射
    ObjectiveCardMapper.cs   - 目标卡映射
  UI/             - 配置面板
    Pages/        - 各配置页面（Connection, Narrative, Knowledge, Advanced, Debug, Prompt）
    Helpers/      - UI 绘制原语（UIHelper.cs）
    Models/       - UI 数据模型
  Settings/       - Mod 设置入口
  Tool/           - 自检工具（RimLifeSelfTest.cs）

Defs/             - RimWorld XML 定义（Hediff 等）
Patches/          - XML 补丁（Trait 扩展等）
Languages/        - 多语言（ChineseSimplified）
Libs/             - 外部依赖 DLL（Bubbles 等）
1.6/Assemblies/   - RimWorld 1.6 编译产物 + NPCLife.dll
```

## 框架依赖：NPCLife

本项目的核心框架能力来自 **NPCLife**，通过 Git submodule（`ext/NPCLife`）以 ProjectReference 源码引用。submodule pin 住特定 commit，确保构建可复现。

**访问边界**：NPCLife 源码虽然可见，RimLife 应只使用其公共 API，不依赖 internal 类或实现细节。文档和知识库不应扫描或记录 NPCLife 的内部实现。

RimLife 实际使用的 NPCLife 公共能力：

| 能力 | RimLife 中的使用方式 |
|---|---|
| Agent 循环 | `AgentLoop` — 创建导演/编剧/即兴编剧 Agent 实例，绑定工作空间与系统提示词 |
| 工作空间管理 | `IWorkspaceManager` / `IWorkspace` — 创建/查询工作空间、管理事件池与轮次 |
| MCP 工具协议 | `ISkillRegistry` / `McpToolAttribute` — 注册 RimWorld 特定查询工具，供 Agent 调用 |
| 角色工具 | `DirectionMcpTools`（导演）、`WritingMcpTools`（编剧）、`FreelancerMcpTools`（即兴编剧）— 由框架提供，RimLife 不修改 |
| LLM 适配 | `ILlmAccessor` / `ICredentialManager` — 凭证管理、模型发现、API 调用 |
| 台词投递 | `ScriptDeliveryService` / `IScriptConsumer` — 框架推送台词，RimLife 通过 `DialogueConsumer` 消费 |
| 事件与卡结构 | `IGameEvent` / `EventCardImpl` / 各种 Card 类型 — RimWorld 对象映射为框架标准结构 |
| 提示词配置 | `PromptConfig` — 框架持有基础身份，RimLife 通过 `PromptAdditions` 追加游戏侧指令 |
| 序列化与基础工具 | `CardSerializer` / `JsonWriter` / `JsonParser` — 统一序列化与配置持久化 |
| 主线程调度 | `MainThreadDispatcher` — 异步 LLM 回调安全转主线程 |

## 关键入口 / 核心模块

- `Source/Infrastructure/RimLifeHarmony.cs` - Mod 启动入口，注册 ContentProvider、启动 Harmony patch
- `Source/Infrastructure/RimLifeCore.cs` - 核心服务，组装 NPCLife 组件、管理 Agent 生命周期
- `Source/Infrastructure/PromptAdditions.cs` - 游戏侧附加指令模型（NPCLife 基座身份不可编辑，此处仅追加）
- `Source/Infrastructure/RimWorldAgentDriver.cs` - 每帧驱动，定时器脉冲 + 主线程回调
- `Source/Data/Events/EventHooks.cs` - Harmony 补丁，拦截 RimWorld 事件注入事件池
- `Source/Mappers/EventCardMapper.cs` - 将 RimWorld 事件转化为 NPCLife IGameEvent
- `Source/Infrastructure/Mcp/` - RimWorld 特定 MCP 工具集
- `Source/UI/ConfigPanelWindow.cs` - 配置面板入口
- `Source/UI/ConfigPanelLayout.cs` - 三区布局（侧栏 + 内容区 + 状态栏）
- `Source/UI/Helpers/UIHelper.cs` - UI 绘制辅助（间距/颜色/按钮/卡片/分段选择器等统一原语）
- `Prompts/` - 三个 Agent 角色的系统提示词（由 NPCLife 持有，RimLife 不可编辑）

## 运行与预览

- 本项目为 RimWorld Mod，无法独立运行或预览
- 构建: `dotnet build RimLife.sln`
- 测试: `dotnet test RimLife.Tests/`
- 部署: 将编译产物放入 RimWorld Mods 目录

## 用户偏好与长期约束

- UI 使用统一的布局原语（UIHelper 中的间距常量、按钮尺寸常量、DrawButtonRow、DrawSegmentedSelector 等）
- 按钮尺寸必须使用 UIHelper 中的常量（BtnWidthSmall/Medium/Large, BtnHeight），禁止硬编码像素值
- 删除操作必须使用两步确认（首次点击标记待删除态，再次点击执行）
- API Key 编辑时必须提供显示/隐藏切换，默认脱敏显示
- 状态消息最后 1 秒应有淡出效果
- 侧栏导航项必须有 hover 反馈
- 卡片必须有四边完整边框（Widgets.DrawBox）
- 禁止在代码中硬编码模型名称作为默认值（如 "gpt-4o"），模型名必须由用户输入或从 API 发现
- ConnectionPage 信息架构：状态摘要（顶部）→ 凭证管理（中部）→ 模型选择（底部），用户应能一眼判断 LLM 是否可用

## 常见问题和预防

- `Text.CalcSize()` 需要在绘制上下文中调用，不能在非 GUI 线程使用
- `Widgets.DrawBox` 绘制的是 1px 边框线，`Widgets.DrawBoxSolid` 绘制实心矩形
- RimWorld IMGUI 是即时模式，每帧重绘，状态需要手动维护
- `Listing_Standard` 的 `GetRect()` 会自动推进游标，不需要手动计算 Y 坐标
