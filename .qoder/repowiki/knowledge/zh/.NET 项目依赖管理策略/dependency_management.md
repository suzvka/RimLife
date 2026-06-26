## 1. 核心系统与工具
该项目采用 **.NET SDK (MSBuild)** 作为核心的依赖管理与构建系统。通过 `.csproj` 文件声明项目结构、目标框架及第三方库引用，利用 **NuGet** 进行包的分发与版本控制。

- **包管理器**: NuGet (配置于 `nuget.config`)
- **构建工具**: MSBuild / dotnet CLI
- **子模块管理**: Git Submodules (用于管理核心框架 `NPCLife`)

## 2. 关键文件与包
- **`RimLife.csproj`**: 主模组项目文件。定义了针对 RimWorld 游戏环境的特定依赖（如 `Krafs.Rimworld.Ref`, `Lib.Harmony.Ref`）以及本地库引用（`Bubbles.dll`）。
- **`ext/NPCLife/src/NPCLife/NPCLife.csproj`**: 核心叙事框架项目文件。作为通用库，它仅依赖基础的 `System.Net.Http`，并配置了多目标框架支持 (`net48; netstandard2.0`)。
- **`nuget.config`**: 显式配置了唯一的包源为官方 `nuget.org`，确保了依赖来源的纯净性与一致性。
- **`.gitmodules`**: 声明了 `ext/NPCLife` 为 Git 子模块，指向外部 GitHub 仓库。这种设计实现了业务逻辑（RimLife）与核心引擎（NPCLife）的物理隔离与独立演进。
- **`dotnet-tools.json`**: 目前为空，但预留了 .NET 全局/局部工具（如代码分析器、格式化工具）的配置空间。

## 3. 架构与约定
- **分层依赖架构**:
    - **应用层 (RimLife)**: 依赖于游戏特定的 API 引用和 Harmony 补丁库。通过 `<ProjectReference>` 直接引用子模块中的 NPCLife 项目，实现源码级集成。
    - **核心层 (NPCLife)**: 保持高度的游戏无关性（Game-agnostic），仅依赖 .NET 标准库。这种设计使得 NPCLife 可以被其他游戏或平台复用。
- **版本控制策略**:
    - **游戏引用**: 使用通配符版本（如 `$(GameVersion).*-*`）来适配不同版本的 RimWorld，提高了模组的兼容性。
    - **测试框架**: 统一使用 `xunit` (v2.9.3) 和 `Microsoft.NET.Test.Sdk`，确保测试环境的一致性。
- **本地库处理**: 对于非 NuGet 发布的第三方库（如 `Bubbles.dll`），采用 `<Reference>` 配合 `<HintPath>` 的方式从 `Libs/` 目录加载，并通过条件编译 (`EnableBubbles`) 控制其启用状态。

## 4. 开发者规范
- **子模块同步**: 在克隆或更新仓库后，必须执行 `git submodule update --init --recursive` 以确保 `NPCLife` 框架代码到位。
- **依赖添加**: 
    - 若为通用功能，应优先考虑添加到 `NPCLife` 项目中。
    - 若为 RimWorld 特有功能，则添加到 `RimLife` 项目。
    - 所有 NuGet 包必须通过 `dotnet add package` 命令添加，以自动维护版本约束。
- **私有源限制**: 当前 `nuget.config` 禁止了除官方源以外的任何包源。若需引入内部包，需先修改该配置文件并经过团队评审。
- **目标框架一致性**: 注意 `RimLife` 锁定在 `net48` 以兼容 Unity/RimWorld 环境，而 `NPCLife` 同时支持 `netstandard2.0` 以保持跨平台潜力。开发时应避免在 `NPCLife` 中引入仅存在于 `net48` 的 API。