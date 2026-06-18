## 项目概述

RimLife 是一个 RimWorld 游戏模组（Mod），作为 [NPCLife](../NPCLife) 框架的**游戏侧适配层**，负责：
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
- 框架依赖: **NPCLife** (NuGet 本地源 `C:\LocalNuGet\`)
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

Prompts/          - 三个 Agent 角色的系统提示词
  DirectorPrompt.txt    - 导演 Agent
  ScreenwriterPrompt.txt - 编剧 Agent
  FreelancerPrompt.txt  - 临时工 Agent

Defs/             - RimWorld XML 定义（Hediff 等）
Patches/          - XML 补丁（Trait 扩展等）
Languages/        - 多语言（ChineseSimplified）
Libs/             - 外部依赖 DLL（Bubbles 等）
1.6/Assemblies/   - RimWorld 1.6 编译产物 + NPCLife.dll
```

## 框架依赖：NPCLife

本项目的核心框架能力来自 **NPCLife**（`E:\NPCLife`），通过 NuGet 包（本地源 `C:\LocalNuGet\`）引用。NPCLife 提供：

| 模块 | 路径 | 说明 |
|---|---|---|
| AgentLoop | `Agent/AgentLoop.cs` | Agent 循环引擎（Drain → Prompt → LLM → 工具调用） |
| MCP 基础设施 | `Framework/Mcp/` | Skill 注册表、工具调用、序列化 |
| Workspace 管理 | `Workspace/` | 工作空间生命周期、事件池、阈值触发 |
| 三角色工具 | `Workspace/DirectionMcpTools.cs` | 导演工具（create_workspace, route_events 等） |
|  | `Workspace/WritingMcpTools.cs` | 编剧工具（push_line, finish_round 等） |
|  | `Workspace/FreelancerMcpTools.cs` | 临时工工具 |
| LLM 适配 | `Infrastructure/Llm/` | OpenAI / Anthropic API 适配器 |
| 事件系统 | `Framework/EventBus.cs` | 事件总线 |
| 台词推送 | `Infrastructure/ScriptDeliveryService.cs` | 台词解析与投递 |

## 关键入口 / 核心模块

- `Source/Infrastructure/RimLifeHarmony.cs` - Mod 启动入口，注册 ContentProvider、启动 Harmony patch
- `Source/Infrastructure/RimLifeCore.cs` - 核心服务，组装 NPCLife 组件、管理 Agent 生命周期
- `Source/Infrastructure/RimWorldAgentDriver.cs` - 每帧驱动，定时器脉冲 + 主线程回调
- `Source/Data/Events/EventHooks.cs` - Harmony 补丁，拦截 RimWorld 事件注入事件池
- `Source/Mappers/EventCardMapper.cs` - 将 RimWorld 事件转化为 NPCLife IGameEvent
- `Source/Infrastructure/Mcp/` - RimWorld 特定 MCP 工具集
- `Source/UI/ConfigPanelWindow.cs` - 配置面板入口
- `Source/UI/ConfigPanelLayout.cs` - 三区布局（侧栏 + 内容区 + 状态栏）
- `Source/UI/Helpers/UIHelper.cs` - UI 绘制辅助（间距/颜色/按钮/卡片/分段选择器等统一原语）
- `Prompts/` - 三个 Agent 角色的系统提示词

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
