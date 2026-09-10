# AGENTS.md

本文件适用于整个仓库。执行任务前先阅读本文件以及所修改目录中的 README；若更深层目录以后增加了 `AGENTS.md`，以更深层文件为准。

## 项目概览

- 这是 Unity 6000.5.5f1 项目，主要语言为 C#，渲染管线为 URP。
- 热更新栈固定为 HybridCLR 8.14.1 + YooAsset 3.0.5。升级 Unity、HybridCLR、YooAsset 或 Input System 前，必须先说明兼容性影响，不能顺手升级依赖。
- `Assets/framework` 编译为 `Haven.Framework`，属于随 Player 发布、不可热替换的 AOT 基础层。
- `Assets/Hotfix` 编译为 `Haven.Hotfix`，属于可更新的业务层。
- 依赖方向必须保持为 `Haven.Hotfix -> Haven.Framework -> HybridCLR.Runtime/YooAsset`。Framework 不得引用 Hotfix；Framework 通过 `IHotfixEntry` 契约和配置中的类型名加载入口。
- 根目录的 `.docx` 是规划、需求和设计文档，不是构建输入。只有任务明确要求同步文档时才修改它们。

## 目录职责

- `Assets/framework/Runtime/Bootstrap`：框架自动启动、重试、关闭和 Unity 生命周期转发。
- `Assets/framework/Runtime/Core`：日志、结果/错误、事件总线、服务注册表、对象池和安全协程。
- `Assets/framework/Runtime/Resources`：YooAsset 资源加载及租约释放。
- `Assets/framework/Runtime/HotUpdate`：内容更新、AOT 元数据加载和热更程序集加载。
- `Assets/framework/Editor`：项目初始化、HybridCLR 生成、DLL 复制及配置校验工具；只能依赖 Editor 程序集。
- `Assets/framework/Tests/Editor`：NUnit/Unity Test Framework 测试。
- `Assets/Hotfix/Runtime/HotfixEntry.cs`：唯一热更业务入口。
- `Assets/Hotfix/Runtime/Modules`：模块注册、初始化与逆序关闭。
- `Assets/Hotfix/Runtime/Flow`：游戏流程状态机。
- `Assets/Hotfix/Runtime/Services`：网络、LLM/AIGC 等外部能力的可替换接口。
- `Assets/Hotfix/Content`：由 YooAsset 收集的热更业务资源。
- `Assets/Hotfix/Generated` 与 `Assets/HybridCLRGenerate`：生成物；不要手工修改其中的 DLL、`*.bytes`、AOT 引用或 `link.xml`。
- `ProjectSettings`、`Packages`：Unity 项目和包管理配置。只做任务所需的最小改动。

## 实现约定

- 新的可变业务逻辑优先放在 `Assets/Hotfix`；只有启动、资源、加载器或跨热更边界的稳定契约才放在 `Assets/framework`。
- Gameplay、UI 和业务模块不得直接依赖网络 SDK、模型 SDK 或 YooAsset；通过服务接口、`IServiceRegistry` 和 `IEventBus` 解耦。
- 增加热更模块时继承 `HotfixModuleBase`，在 `HotfixEntry.Initialize` 注册。用 `Order` 控制初始化顺序，并确保关闭时可安全逆序释放。
- 跨模块通知优先使用明确的事件类型；共享能力通过接口注册。不要新增静态全局状态来绕过现有上下文。
- 资源加载后必须释放对应租约；订阅事件后必须保存并释放订阅对象；实现 `IDisposable` 的服务必须能被 `ServiceRegistry.Clear` 安全清理。
- 协程中的异常必须进入现有失败路径；嵌套协程优先通过 `SafeCoroutine` 执行。不要静默吞掉异常。
- 可恢复错误使用 `FrameworkError`/`FrameworkResult`，提供稳定的错误码、模块名和 `CanRetry`；日志使用 `GameLog`，不得记录令牌、密钥、完整用户隐私数据或内部服务凭据。
- 不要更改 `Haven.Hotfix.HotfixEntry` 的全名、程序集名、YooAsset 包名或生成文件地址，除非同时迁移设置、加载代码、编辑器校验和文档。

## C# 与 Unity 风格

- 遵循现有格式：4 空格缩进、Allman 大括号、一个文件一个主要类型、显式访问级别。
- 类型、方法、属性和公开成员使用 PascalCase；私有字段使用 `_camelCase`；序列化字段保持 `private` 并加 `[SerializeField]`。
- 接口以 `I` 开头；能封闭继承的实现类使用 `sealed`；只读状态优先通过只读属性暴露。
- 命名空间沿用 `Haven.Framework.*` 或 `Haven.Hotfix.*`，并与程序集职责匹配。
- 注释解释约束或原因，不复述代码。避免无关格式化和大范围重排。
- Unity 资源的 `.meta` 与资源是一体的：移动、重命名或删除资源时同时处理对应 `.meta`；不要手写或复用 GUID。新增资源必须在 Unity 导入后连同 `.meta` 提交。
- 不提交 `Library`、`Temp`、`Obj`、`Build`、`Builds`、`Logs`、`UserSettings`、`HybridCLRData`、IDE 工程文件或其他已忽略的本地产物。

## 验证与测试

Unity 编译结果是准绳；不要用普通 `dotnet test` 代替 Unity 的程序集解析和测试运行。Windows 批处理示例（把 `<UNITY>` 替换为 Unity 6000.5.5f1 的 `Unity.exe` 路径）：

```powershell
& "<UNITY>" -batchmode -nographics -projectPath . `
  -executeMethod Haven.Framework.Editor.HavenFrameworkSetup.ValidateProject `
  -logFile Logs/ValidateProject.log -quit

& "<UNITY>" -batchmode -nographics -projectPath . `
  -runTests -testPlatform EditMode -testResults Logs/EditModeResults.xml `
  -logFile Logs/EditModeTests.log
```

- 修改核心容器、事件、流程、启动或热更边界时，在 `Assets/framework/Tests/Editor` 增加或更新 NUnit 测试。
- 至少覆盖成功路径、失败路径、重复调用/清理和无效状态转换；异步启动代码还要覆盖超时或异常传播。
- 日常业务开发使用 `EditorDirect`。涉及更新链路时，还应按改动范围验证 `EditorSimulate`、`Offline` 或 `Host`。
- 若当前环境没有匹配的 Unity Editor，应明确说明哪些验证未运行，不能把静态检查描述为 Unity 编译通过。

## HybridCLR 与发布规则

- 仅修改 Hotfix 代码并需要刷新热更 DLL 时，可运行菜单 `Haven/Framework/Compile Hotfix DLL Only`。
- 修改 Framework、泛型/AOT 引用、热更程序集列表或切换 Windows/Android/iOS/WebGL 构建目标后，必须针对当前目标运行 `Haven/Framework/3. Generate All and Prepare DLL Assets`。
- 首次配置依次运行 `1. Setup Project`、`2. Install HybridCLR Runtime`、`3. Generate All and Prepare DLL Assets`；不要手工模拟生成结果。
- 生成后检查 `Assets/Hotfix/Generated` 中的热更 DLL 和配置要求的 AOT DLL，并通过 `Haven/Framework/Validate Project`。
- 发布资源通过 `YooAsset/AssetBundle Builder` 构建 `DefaultPackage`。`Host` 模式的远端路径默认是 `{host}/{platform}/{appVersion}`。
- `Assets/Resources/HavenHotUpdateSettings.asset` 不得存放 API 密钥；切换到 `Host` 前确认主/备用地址、应用版本和平台后缀策略。

## 完成任务前

1. 查看 `git diff --check` 和 `git status --short`，确认没有无关资源、缓存或意外生成物。
2. 运行与改动范围相称的 Unity 校验和测试，并记录未能运行的项目。
3. 若改动影响程序集边界、入口、生成步骤或发布流程，同步更新 `Assets/framework/README.md`。
4. 若改动资源或场景，确认对应 `.meta` 已包含且引用没有丢失。
5. 若改动热更新或发布链路，说明测试过的 Play Mode、目标平台与生成步骤。
