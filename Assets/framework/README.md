# Haven HybridCLR 热更新框架

本目录是不会被热替换的 AOT 基础层；可变业务代码位于 `Assets/Hotfix`，编译为 `Haven.Hotfix.dll`。框架固定使用 HybridCLR 8.14.1 与 YooAsset 3.0.5，适配当前 Unity 6000.5.5f1 工程。

## 目录与职责

```text
Assets/
├─ framework/
│  ├─ Runtime/Bootstrap/       自动启动、失败与重试、生命周期转发
│  ├─ Runtime/Core/            日志、错误、事件、服务容器、对象池
│  ├─ Runtime/Resources/       YooAsset 资源与原生文件加载
│  ├─ Runtime/HotUpdate/       版本、Manifest、下载、AOT 元数据、程序集加载
│  └─ Editor/                  一键配置、生成与校验工具
└─ Hotfix/
   ├─ Runtime/HotfixEntry.cs   唯一业务入口
   ├─ Runtime/Core/            热更模块宿主
   ├─ Runtime/Flow/            游戏流程状态机
   ├─ Runtime/Modules/         业务模块注册
   ├─ Runtime/Services/        Network/AIGC 可替换接口
   ├─ Generated/               生成的 *.dll.bytes 与 AOT 元数据
   └─ Content/                 需要由 YooAsset 更新的业务资源
```

依赖方向固定为：`Haven.Hotfix -> Haven.Framework -> HybridCLR.Runtime/YooAsset`。Framework 不引用 Hotfix；入口由字符串定位并通过 `IHotfixEntry` 契约调用。

## 启动状态机

1. 创建事件总线、服务容器和资源服务。
2. 初始化 YooAsset 包。
3. 请求版本并加载 Manifest。
4. 计算并下载资源，发布进度事件，失败时按配置重试。
5. 用 `HomologousImageMode.SuperSet` 加载 AOT 补充元数据。
6. 按配置顺序 `Assembly.Load` 热更程序集。
7. 创建 `Haven.Hotfix.HotfixEntry`，初始化热更模块并转发生命周期。

`HotUpdateProgress`、`HotUpdateFailed`、`HotUpdateCompleted` 都通过 `IEventBus` 发布，可由补丁 UI 直接订阅。所有错误包含模块、错误码、可重试标志和内部异常；用户提示层不需要展示内部堆栈。

资源字节加载兼容 LegacyBuildPipeline 的 TextAsset 包与原始管线的 RawFileObject；`.rawfile` 扩展名不代表实际的包类型。YooAsset 句柄由资源层显式持有、释放，不能直接交给会 Dispose 嵌套枚举器的 SafeCoroutine。营地任务使用 JsonUtility 泛型，AOT 元数据配置同时包含 `UnityEngine.JSONSerializeModule.dll`。

## 首次接入

Unity 完成包导入后，先在 Unity Hub 安装实际发布平台的 **Build Support 和 IL2CPP** 模块，再按顺序执行：

1. `Haven/Framework/1. Setup Project`：创建运行时配置、设置 IL2CPP、注册 `Haven.Hotfix`、配置 YooAsset 收集器。
2. `Haven/Framework/2. Install HybridCLR Runtime`：安装与 Unity 6000.5 对应的本地 `il2cpp_plus`。
3. `Haven/Framework/3. Generate All and Prepare DLL Assets`：生成热更 DLL、桥接、link.xml、AOT 元数据，并复制为 YooAsset 原生文件。
4. `Haven/Content/1. Build Current Assets and Publish Locally`：构建 `DefaultPackage` 并发布到 ASP.NET 网关的静态目录。
5. `Haven/Build/Windows Client Offline (HybridCLR)`：自动执行 Generate All、制作随包资源并构建不依赖补丁服务器的开发客户端。
6. `Haven/Build/Windows Client Hosted (HybridCLR)`：正式 LAN 首包；必须显式提供 `HAVEN_CONTENT_VERSION`、`HAVEN_PATCH_BASE_URL` 和 `HAVEN_GAME_SERVER_HOST`，且服务器地址必须是同一个非回环私人 IPv4。

Windows Dedicated Server 使用 `Haven/Build/Windows Dedicated Server` 单独构建。工具会临时关闭 HybridCLR；服务端规则更新需要重新构建与重启，不通过客户端下载补丁发布。

HybridCLR 的 AOT 裁剪结果和补充元数据与构建目标绑定：切换 Windows、Android、iOS 或 WebGL 后，必须针对新目标重新执行第 3 步；Framework 中可能影响 AOT 引用的代码变更后也应重新生成。

远端默认目录规则为：`{host}/{platform}/{appVersion}`，项目中提交的默认值是 `http://127.0.0.1:5080/patches/PC/0.1.0`。Hosted 构建设置 `HAVEN_PATCH_BASE_URL=http://<SERVER_LAN_IP>:5080/patches`、`HAVEN_GAME_SERVER_HOST=<SERVER_LAN_IP>` 和显式内容版本；构建脚本只在本次构建中注入并于结束后恢复，避免提交局域网 IP。正式 LAN 发布使用 `scripts/build-lan-release.ps1`，并由 `docs/HOTUPDATE_LAN_ACCEPTANCE.md` 记录 V1/V2、故障重试和双机验收步骤。

## 开发模式

- `EditorDirect`：编辑器直接使用已编译的 `Haven.Hotfix`，不要求先构建资源包，适合日常业务开发。
- `EditorSimulate`：运行 YooAsset 模拟构建，验证地址与依赖。
- `Offline`：使用安装包内置 Manifest/资源。
- `Host`：完整执行版本、Manifest、下载与热更流程。

## 增加业务模块

营地委托与管事聊天模块使用 `Runtime/CampQuests` 的稳定契约，任务与聊天会话逻辑在 `Haven.Hotfix`，旧 SurvivalEngine 适配器及本机 Gateway 客户端在默认程序集 `Assets/HavenCamp/Runtime`；Hotfix 不引用 Assembly-CSharp。聊天只返回文字，不持久化或执行物品交换。只有 `WorldGenMap` 安装适配器时才启用此模块。该场景自带非持久 GameBootstrap，支持旧菜单的场景重载；联机原型沿用原启动方式。

独立入口 `Haven/Build/Windows Camp Quest Demo` 针对 Windows Player 执行 Generate All，制作随包内容并临时使用 Offline，结束后恢复原设置。它只更新 StreamingAssets，不再切换 Hosted 补丁服务器的远端版本指针。新增契约改变 AOT 边界，必须重新完整构建，不用于旧客户端原地升级。详细说明与验收状态见 `docs/CAMP_QUESTS.md`。

0.2.0 合作玩法通过 `ICoopGameplayService` 暴露稳定契约，由 `FishNetGameplayService` 在服务器维护权威状态。客户端只提交采集、制作、建造、贡献、攻击和领奖意图；服务器用玩家网络对象位置校验距离，并独立结算私人背包、建筑、敌人生命与房间共享委托。每次有效变更向房内成员广播按接收者裁剪的完整快照；晚加入玩家会单独加载至 `WorldGenMap` 并收到当前世界。请求 ID 去重、操作冷却、资源耗尽/刷新、建筑地形和碰撞校验及奖励领取标记共同防止重复结算。游戏内右侧 WP4 面板可直接验收。详细步骤见 `doc/08_联机分工计划.md` 和 `doc/08_WP4_联机玩法同步说明.md`。

1. 在 `Assets/Hotfix/Runtime/Modules` 新建 `HotfixModuleBase` 子类。
2. 通过 `Context.Services` 注册接口，通过 `Context.Events` 发布领域事件。
3. 在 `HotfixEntry.Initialize` 中按需添加模块。`Order` 越小越先初始化，关闭时按相反顺序释放。
4. UI 只调用应用服务或订阅事件；Gameplay 不直接依赖网络 SDK、模型 SDK 或 YooAsset。
5. 热更程序集之间如有依赖，在 `HotUpdateSettings` 中按“被依赖程序集在前”的顺序配置。

## 发布检查

- `Haven/Framework/Validate Project` 无错误。
- HybridCLR Installer 已完成，`Generate/All` 已针对实际目标平台运行。
- `Generated` 中包含所有热更 DLL 和配置中的 AOT DLL，YooAsset 收集规则为 `PackRawFile`。
- 验证无更新、有更新、下载失败重试、旧资源回退四条路径。
- 每次发布绑定客户端版本、资源版本、Git 标签与构建目标；不要把 API 密钥写入客户端配置或日志。
- 内容发布会先复制并验证新文件，最后切换 `DefaultPackage.version`；旧 Manifest 与哈希命名文件保留，便于回退。不要在服务器运行期间手工清空补丁目录。
- `start-gateway.ps1 -SimulatePatchDownloadFailure` 只用于验收：版本和 Manifest 仍可访问，资源 payload 返回 503，以稳定验证 Player 的失败和手动重试路径。
