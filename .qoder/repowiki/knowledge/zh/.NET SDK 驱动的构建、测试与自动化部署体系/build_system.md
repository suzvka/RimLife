该项目采用标准的 **.NET SDK** 作为核心构建系统，通过 **MSBuild** 驱动编译、打包及自动化部署流程。项目结构呈现为“宿主模组（RimLife）+ 核心框架（NPCLife）”的双层架构，两者均通过 `.csproj` 进行依赖管理与版本控制。

### 1. 构建系统与工具链
- **核心框架**：使用 `Microsoft.NET.Sdk`，目标框架锁定为 `net48`（.NET Framework 4.8），以兼容 RimWorld 游戏引擎的运行环境。
- **依赖管理**：通过 `nuget.config` 配置官方源，利用 `PackageReference` 引入 `Krafs.Rimworld.Ref`（游戏引用）、`Lib.Harmony.Ref`（补丁库）以及 `xunit`（测试框架）。
- **子模块集成**：`NPCLife` 作为 Git 子模块存在于 `ext/` 目录下，通过 `<ProjectReference>` 在编译时动态链接，实现了叙事逻辑与游戏适配层的解耦。

### 2. 自动化部署机制 (Post-build Targets)
项目在 `RimLife.csproj` 中定义了高度自动化的部署逻辑，利用 MSBuild 的 `AfterTargets="Build"` 钩子实现“编译即安装”：
- **跨平台支持**：通过 `$(OS)` 变量判断操作系统，分别调用 `WinDeploy`（Windows）和 `MacDeploy`（macOS）目标。
- **增量同步**：
  - **Windows**：使用 `robocopy /MIR` 命令将 `About`、`Defs`、`Assemblies` 等目录镜像同步至 RimWorld 的 `Mods` 文件夹。
  - **macOS**：使用 `rsync -a --delete --checksum` 确保文件完整性并同步至 `RimWorldMac.app` 内部路径。
- **环境变量驱动**：部署路径由 `$(RimWorldDir)` 环境变量控制，若未设置则跳过部署，确保了构建脚本在不同开发机上的兼容性。

### 3. 测试体系
- **单元测试**：`RimLife.Tests` 项目基于 **xUnit** 框架，针对核心记忆数据结构（如 `PawnMemoryDataStructure`）及巩固逻辑（`MemoryConsolidator`）进行验证。
- **内部可见性**：主项目通过 `InternalsVisibleTo` 属性向测试项目开放内部成员，便于对非公开 API 进行深度测试。

### 4. 外部依赖获取脚本
在 `Libs/` 目录下提供了 `Bubbles.ps1` 和 `Bubbles.sh` 脚本，用于从 GitHub Release 自动抓取并解压第三方依赖库 `Bubbles.dll`。这种设计避免了将二进制大文件直接提交到版本控制系统，同时简化了环境初始化流程。

### 5. 开发者规范
- **构建命令**：推荐使用 `dotnet build` 或 Visual Studio 进行编译，编译成功后模组会自动出现在游戏目录中。
- **版本控制**：通过 `<GameVersion>` 属性动态定义编译常量（如 `V1_6`），支持针对不同游戏版本的差异化编译。
- **配置隔离**：敏感或本地化路径（如游戏安装目录）应通过环境变量 `RIMWORLD_DIR` 注入，严禁硬编码在工程文件中。