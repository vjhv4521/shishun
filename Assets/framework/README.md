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

## 首次接入

Unity 完成包导入后，先在 Unity Hub 安装实际发布平台的 **Build Support 和 IL2CPP** 模块，再按顺序执行：

1. `Haven/Framework/1. Setup Project`：创建运行时配置、设置 IL2CPP、注册 `Haven.Hotfix`、配置 YooAsset 收集器。
2. `Haven/Framework/2. Install HybridCLR Runtime`：安装与 Unity 6000.5 对应的本地 `il2cpp_plus`。
3. `Haven/Framework/3. Generate All and Prepare DLL Assets`：生成热更 DLL、桥接、link.xml、AOT 元数据，并复制为 YooAsset 原生文件。
4. 在 `YooAsset/AssetBundle Builder` 中构建 `DefaultPackage`，把输出部署到配置的远端目录。
5. 将 `Assets/Resources/HavenHotUpdateSettings.asset` 从 `EditorDirect` 改为 `Host`，设置主/备用资源地址与应用版本。

HybridCLR 的 AOT 裁剪结果和补充元数据与构建目标绑定：切换 Windows、Android、iOS 或 WebGL 后，必须针对新目标重新执行第 3 步；Framework 中可能影响 AOT 引用的代码变更后也应重新生成。

<<<<<<< Updated upstream
远端默认目录规则为：`{host}/{platform}/{appVersion}`，例如 `http://127.0.0.1:8080/CDN/PC/v1.0`。可在配置中关闭平台和版本后缀。
=======
远端默认目录规则为：`{host}/{platform}/{appVersion}`。编辑器保持 `EditorDirect`；在私人局域网中运行 `scripts/setup-lan-hotupdate.ps1 -SetPrivateProfile` 后，脚本会把主、备用 Host 更新为服务器当前内网地址，最终请求形如 `http://192.168.x.x:5080/patches/PC/0.1.0`。可在配置中关闭平台和版本后缀。
>>>>>>> Stashed changes

## 开发模式

- `EditorDirect`：编辑器直接使用已编译的 `Haven.Hotfix`，不要求先构建资源包，适合日常业务开发。
- `EditorSimulate`：运行 YooAsset 模拟构建，验证地址与依赖。
- `Offline`：使用安装包内置 Manifest/资源。
- `Host`：完整执行版本、Manifest、下载与热更流程。

## 增加业务模块

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

## 双人局域网发布约定

- `ZENG` 是唯一发布服务器，Gateway 仅在私人 LAN 的 TCP `5080` 上提供 `/health` 与 `/patches`。
- 只有发布者覆盖 `Build/LocalServer/patches`；合作伙伴提交源码并下载补丁，`Build/` 不进入 Git。
- 资源变化执行 `Haven/Content/1. Build Current Assets and Publish Locally`；`Assets/Hotfix` 代码变化执行 `Haven/Content/2. Compile Hotfix and Publish Locally`。
- Framework、Unity/包版本、AOT 契约或原生配置变化时提升 App Version，重新 Generate All、构建补丁与完整客户端。
- 完整操作步骤及验收命令见仓库根目录 `docs/LAN_HOT_UPDATE.md`。
