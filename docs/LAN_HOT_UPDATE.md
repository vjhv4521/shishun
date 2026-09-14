# Haven 两人局域网热更新

电脑 `ZENG` 运行 ASP.NET Gateway，使用 TCP `5080` 托管 `Build/LocalServer/patches/PC/0.1.0`；后续双人玩法另用 FishNet UDP `7770`。只在自己的私人路由器或手机热点上开放，绝不做公网端口映射。

## 1. 配置私人局域网

两台电脑先接入同一个私人路由器/热点，且不是 Guest 网络。在 `ZENG` 以管理员身份打开 PowerShell：

```powershell
Set-Location "C:\Users\26906\Desktop\第一批_立项与设计\shishun"
.\scripts\setup-lan-hotupdate.ps1 -DiscoverOnly
```

核对输出的网络名称。如果已是 `Private`，运行 `./scripts/setup-lan-hotupdate.ps1`。如果是自己的新热点且类别为 `Public`，用检测到的网络名称明确确认后运行：

```powershell
.\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile -ExpectedNetworkName "你的私人热点名称"
```

脚本只设置网络类别和 `Private`/`LocalSubnet` 的 TCP `5080` 防火墙规则，不修改 Unity 资产。**当前若仍显示校园公共 Wi-Fi，请停止，切勿对它运行第二条命令。** 开始双人玩法联调时再运行同一脚本的 `-EnableGameplay`，额外开放同范围的 UDP `7770`。

建议在路由器里为服务器保留 DHCP 地址；记下脚本打印的 `<SERVER_LAN_IP>`。

## 2. 本机启动与双方验收

先在 Unity 执行 `Haven/Content/1. Build Current Assets and Publish Locally`。普通 PowerShell 启动：

```powershell
Set-Location "C:\Users\26906\Desktop\第一批_立项与设计\shishun"
.\scripts\start-gateway.ps1
```

没有 DeepSeek Key 的警告不影响补丁下载。保持此窗口开启，`Ctrl+C` 停止。本机另开 PowerShell：

```powershell
.\scripts\test-lan-hotupdate.ps1 -ServerAddress 127.0.0.1
```

合作伙伴在自己的电脑执行：

```powershell
Test-NetConnection <SERVER_LAN_IP> -Port 5080
Invoke-WebRequest http://<SERVER_LAN_IP>:5080/patches/PC/0.1.0/DefaultPackage.version
```

如果也有本仓库，可用 `./scripts/test-lan-hotupdate.ps1 -ServerAddress <SERVER_LAN_IP>`。测试必须看到 `status=ok`、`patchHosting=True` 且版本文件非空。

## 3. 制作双方共用的首包

Unity Hub 必须为 Unity `6000.5.5f1` 安装 Windows IL2CPP Build Support。构建客户端的 PowerShell 先设置本次构建地址（不存到 Git）：

```powershell
$env:HAVEN_PATCH_BASE_URL = 'http://<SERVER_LAN_IP>:5080/patches'
```

在 Unity 菜单按顺序执行：

1. `Haven/Framework/2. Install HybridCLR Runtime`
2. `Haven/Framework/3. Generate All and Prepare DLL Assets`
3. `Haven/Content/1. Build Current Assets and Publish Locally`
4. `Haven/Build/Windows Client (HybridCLR)`

构建器临时使用 `Host` 和上述地址，完成后恢复提交的 `EditorDirect`/回环配置。将整个 `Build/WindowsClient` 压缩给合作伙伴，双方使用同一首包。`Build/` 不进入 Git。

## 4. 日常补丁发布与回退

只有 `ZENG` 发布。先合并双方代码，等 Unity 编译完成；仅资源变化用 `Haven/Content/1. Build Current Assets and Publish Locally`，`Assets/Hotfix` 代码变化用 `Haven/Content/2. Compile Hotfix and Publish Locally`。每次检查：

```powershell
Get-Content '.\Build\LocalServer\patches\PC\0.1.0\DefaultPackage.version'
```

合作伙伴重新启动客户端以读取新版本，无需替换 EXE。发布器保留旧 Manifest/哈希文件及 `DefaultPackage.version.previous`；若新版本有故障，先停止发布操作并把指针恢复为上一版本，再让客户端重启。不要删除旧补丁目录。

修改 AOT 契约、Unity/FishNet/YooAsset/HybridCLR 版本、PlayerSettings 或原生插件时，提升 `App Version`、重新 Generate All 并重发完整客户端。专用服务器规则变更需重建并重启服务器。

## 5. 故障定位

- TCP 不通：检查同一私人 LAN、非 Guest 网络、Gateway 窗口、防火墙和路由器 AP/Client Isolation。
- TCP 通但版本 404：先发布内容，检查 `Build/LocalServer/patches/PC/0.1.0/DefaultPackage.version`。
- 重启后未更新：核对首包内 Host/App Version、版本指针和客户端 Manifest/下载日志。
- 路由器隔离无法关闭：改用私人虚拟局域网，不开放公网端口。
