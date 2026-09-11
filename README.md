<<<<<<< Updated upstream
# shishun
=======
# Haven：联机生存游戏技术垂直切片

这是一个面向客户端/Unity 开发岗位的可运行技术样板：FishNet 权威服务器、DeepSeek 服务端代理，以及 YooAsset + HybridCLR 资源和代码热更新被串成同一条最小闭环。项目使用 Unity `6000.5.5f1`。

## 已完成的闭环

- FishNet + Tugboat：Windows Dedicated Server，UDP `7770`，最多 4 人。
- 服务器权威移动：客户端只提交 WASD 输入，位置由服务器计算并同步。
- AIGC 安全链路：客户端不能接触 DeepSeek Key；请求经 ServerRpc 到 Dedicated Server，再访问 ASP.NET 网关。
- 结构化任务：只允许 `Collect`、`Wood/Stone`、`Food` 和数量 `1-10`；网关与游戏服会分别校验。
- 可用性保护：请求限流、超时、重试；DeepSeek 不可用时，游戏服返回确定性本地任务。
- 热更新：客户端启动时从网关 `/patches` 获取 YooAsset 清单和 HybridCLR DLL；Dedicated Server 只随版本重建和重启。
- 自动化入口：场景生成、配置校验、热更发布、客户端和服务端构建均有 `Haven` 菜单。

```mermaid
flowchart LR
    C[Unity Client] -->|FishNet UDP 7770<br/>input / ServerRpc| S[Unity Dedicated Server]
    S -->|HTTP + shared token| G[ASP.NET Core Gateway :5080]
    G -->|server-side API key| D[DeepSeek API]
    G -->|/patches/PC/0.1.0| C
    S -->|validated TargetRpc<br/>or local fallback| C
```

关键代码入口：

- `Assets/framework/Runtime/Networking`：FishNet 服务、权威角色、DeepSeek 网关客户端、结果校验。
- `Assets/framework/Runtime/HotUpdate`：YooAsset 更新状态机和 HybridCLR 加载。
- `Assets/framework/Editor/Build`：一键生成演示场景及构建/发布入口。
- `Server/Haven.Gateway`：DeepSeek 代理与热更静态文件服务器。
- `Server/Haven.Gateway.Tests`：网关输入/输出策略测试。

## 第一次运行

### 1. 补齐 Unity 构建模块

在 Unity Hub 中为 `6000.5.5f1` 安装：

- `Windows Build Support (IL2CPP)`：客户端的 HybridCLR 构建需要。
- `Dedicated Server Build Support (Windows)`：服务端构建需要。

当前项目代码、YooAsset 内容构建和测试均已验证；若没有上述两个模块，对应 Player 构建会被 Unity 拒绝。

### 2. 准备 Unity 工程

打开项目后按顺序执行：

1. `Haven/Framework/2. Install HybridCLR Runtime`（首次克隆或升级 Unity/HybridCLR 时）。
2. `Haven/Network/1. Create or Refresh Demo`（演示场景已经生成，修改网络配置后再执行）。
3. `Haven/Framework/Validate Project` 和 `Haven/Network/Validate Demo`。

直接在 Editor 打开 `Assets/Scenes/FrameworkDemo.unity` 可使用 EditorDirect 模式开发业务，不需要启动补丁服务器。

### 3. 启动网关

纯热更新局域网联调不需要 DeepSeek Key。先连接自己的私人路由器或手机热点，以管理员身份完成一次网络、防火墙和客户端地址配置：

```powershell
.\scripts\setup-lan-hotupdate.ps1 -DiscoverOnly
.\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile
```

再用普通 PowerShell 启动网关；脚本会监听 TCP `5080`，并在活动网络不是 `Private` 时拒绝对局域网开放：

```powershell
.\scripts\start-gateway.ps1
```

检查：

```powershell
.\scripts\test-lan-hotupdate.ps1 -ServerAddress 127.0.0.1
```

没有 Key 时网关仍可用于热更托管；AIGC 请求会得到 `503`，随后 Dedicated Server 自动使用本地任务降级。

双人局域网的首包制作、日常发布、合作伙伴验收及故障处理见 [两人局域网热更新指南](docs/LAN_HOT_UPDATE.md)。`Build/` 只在服务器本地托管，不提交 Git。

### 4. 构建内容与程序

Unity 菜单：

- `Haven/Content/1. Build Current Assets and Publish Locally`：构建当前热更内容并发布到 `Build/LocalServer/patches`。
- `Haven/Content/2. Compile Hotfix and Publish Locally`：只更新业务 DLL/内容，不重建客户端。
- `Haven/Build/Windows Dedicated Server`：构建 `Build/WindowsServer/HavenServer.exe`。
- `Haven/Build/Windows Client (HybridCLR)`：Generate All、制作首包内容并构建 `Build/WindowsClient/HavenClient.exe`。

客户端构建时工具会临时把热更模式切为 `Host`，构建结束恢复 EditorDirect；服务端构建时会临时关闭 HybridCLR，避免服务端被强制切成 IL2CPP。

### 5. 本地联调

网关和 Dedicated Server 必须使用相同的共享令牌。在第二个 PowerShell 中启动服务端：

```powershell
$env:HAVEN_GATEWAY_TOKEN = '与 Gateway__SharedToken 相同的值'
./Build/WindowsServer/HavenServer.exe -batchmode -nographics -logFile ./Build/WindowsServer/server.log
```

再运行 `HavenClient.exe`，点击“连接 Dedicated Server”，用 WASD 移动，然后点击“由服务器请求 DeepSeek 生成任务”。局域网联调时，把 `HavenNetworkSettings` 的主机/网关地址改为服务器机器的内网 IP，并放行 TCP `5080` 与 UDP `7770`。

## 验证命令

```powershell
dotnet test ./Server/Haven.Gateway.Tests/Haven.Gateway.Tests.csproj --configuration Release
```

Unity 测试可在 Test Runner 中运行 `EditMode`；当前结果为 9/9。网关策略测试当前为 6/6。

## 发布边界

- `DeepSeek__ApiKey` 只存在于网关进程环境变量。
- `Gateway__SharedToken` / `HAVEN_GATEWAY_TOKEN` 是内网共享密钥，不是玩家身份认证方案；公网部署应再加 HTTPS、反向代理和正式鉴权。
- `Build/`、`Library/`、`HybridCLRData/`、日志和服务端 `bin/obj` 已忽略，不应同步到 GitHub。
- 热更脚本只能引用稳定的 AOT 契约；修改 AOT 类型或升级 Unity 后，要重新 Generate All 并发布新的完整客户端。
>>>>>>> Stashed changes
