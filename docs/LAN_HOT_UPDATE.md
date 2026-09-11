# 两人局域网热更新

本方案让电脑 `ZENG` 作为唯一发布服务器：ASP.NET Gateway 在私人局域网监听 TCP `5080`，并从 `Build/LocalServer/patches/PC/0.1.0` 提供 YooAsset Manifest、资源和 HybridCLR 热更 DLL。现阶段不开放 FishNet `7770`，也不要求配置 DeepSeek。

## 一次性配置服务器

1. 两台电脑连接同一个私人路由器或手机热点。不要使用校园公共 Wi-Fi，也不要连接 Guest/访客网络。
2. 在 `ZENG` 上以管理员身份打开 PowerShell，进入仓库：

   ```powershell
   Set-Location "C:\Users\26906\Desktop\第一批_立项与设计\shishun"
   ```

3. 先只检测当前网络：

   ```powershell
   .\scripts\setup-lan-hotupdate.ps1 -DiscoverOnly
   ```

4. 确认显示的是自己的路由器或手机热点后执行：

   ```powershell
   .\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile
   ```

脚本会把当前网络设为 `Private`，创建或修正防火墙规则 `Haven Patch Server TCP 5080`，并把 `Assets/Resources/HavenHotUpdateSettings.asset` 的主、备用地址写为当前局域网 IP。规则只允许 `Private` 网络的 `LocalSubnet` 访问 TCP `5080`。

建议在路由器管理页为 `ZENG` 保留当前 DHCP 地址。如果地址以后改变，重新运行配置脚本即可。

## 启动和验收服务器

普通 PowerShell 执行：

```powershell
Set-Location "C:\Users\26906\Desktop\第一批_立项与设计\shishun"
.\scripts\start-gateway.ps1
```

脚本默认监听 `http://0.0.0.0:5080`，但发现活动网络不是 `Private` 时会拒绝启动。未配置 DeepSeek Key 的警告不影响补丁托管。保持窗口开启，用 `Ctrl+C` 停止服务器。

本机另开 PowerShell 验证：

```powershell
.\scripts\test-lan-hotupdate.ps1 -ServerAddress 127.0.0.1
```

合作伙伴在同一局域网中，把下面地址替换为启动脚本显示的服务器 IPv4：

```powershell
Test-NetConnection <SERVER_LAN_IP> -Port 5080
Invoke-WebRequest http://<SERVER_LAN_IP>:5080/patches/PC/0.1.0/DefaultPackage.version
```

如果合作伙伴也有仓库，可直接执行：

```powershell
.\scripts\test-lan-hotupdate.ps1 -ServerAddress <SERVER_LAN_IP>
```

## 制作双方共用的首包

先在 Unity Hub 为 Unity `6000.5.5f1` 安装 `Windows Build Support (IL2CPP)`，再在 Unity 菜单按顺序执行：

1. `Haven/Framework/2. Install HybridCLR Runtime`
2. `Haven/Framework/3. Generate All and Prepare DLL Assets`
3. `Haven/Content/1. Build Current Assets and Publish Locally`
4. `Haven/Build/Windows Client (HybridCLR)`

把生成的 `Build/WindowsClient` 整个压缩并交给合作伙伴。双方使用同一份首包；`Build/` 已被 Git 忽略，不上传 GitHub。

配置继续保持 `EditorDirect`，方便编辑器日常开发；构建工具会在构建客户端时临时切换为 `Host`，结束后自动恢复。客户端实际请求路径是：

```text
http://<SERVER_LAN_IP>:5080/patches/PC/0.1.0
```

## 日常发布

只有服务器电脑负责发布：

1. 合并并拉取双方最新 Git 代码，打开 Unity 并等待编译完成。
2. 只有资源变化时，执行 `Haven/Content/1. Build Current Assets and Publish Locally`。
3. `Assets/Hotfix` 代码变化时，执行 `Haven/Content/2. Compile Hotfix and Publish Locally`。
4. 检查发布版本：

   ```powershell
   Get-Content ".\Build\LocalServer\patches\PC\0.1.0\DefaultPackage.version"
   ```

5. 合作伙伴退出并重新启动客户端，即会下载新的 Manifest、资源和热更 DLL，无需替换 EXE。

## 必须重新分发首包的变化

`Assets/Hotfix` 业务逻辑和 YooAsset 收集的资源可以在 `App Version = 0.1.0` 内热更新。以下变化不能只发补丁：

- `Assets/framework` AOT 基础层
- FishNet、HybridCLR 或 YooAsset 包版本
- Unity 版本、PlayerSettings 或原生插件
- AOT 类型、泛型引用或程序集边界

发生这些变化时，把 App Version 升至例如 `0.2.0`，重新执行 Generate All、内容发布和 Windows Client 构建，并重新分发完整首包。配置服务器时同时传入新版本：

```powershell
.\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile -AppVersion 0.2.0
.\scripts\start-gateway.ps1 -AppVersion 0.2.0
```

## 常见故障

- TCP 不通：确认 Gateway 窗口仍开着，双方在同一私人 LAN，且没有使用 Guest Wi-Fi。
- TCP 通但版本文件 404：先在 Unity 发布内容，并检查 `Build/LocalServer/patches/PC/0.1.0/DefaultPackage.version`。
- 重启客户端未变化：确认发布版本已改变、客户端 App Version 一致，并检查客户端日志中的 Manifest 和下载阶段。
- 路由器启用了客户端隔离：关闭 AP/Client Isolation；不能关闭时改用 Tailscale，仍不要开放公网端口。
