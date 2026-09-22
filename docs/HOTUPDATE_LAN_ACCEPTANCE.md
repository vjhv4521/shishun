# Hosted 热更新与局域网验收手册

本手册用于 Windows `0.1.0` 演示首包。正式验收只允许在自己的路由器或手机热点中进行，网络配置必须是 `Private`。不要在校园或其他公共 WLAN 上开放端口。

## 1. 发布约束

| 项目 | 基线 | 补丁 |
| --- | --- | --- |
| Git 标签 | `demo-client-v0.1.0` | `demo-patch-v0.1.0-hotfix.1` |
| 内容版本 | `0.1.0-baseline.1` | `0.1.0-hotfix.1` |
| 允许修改 | 完整客户端基线 | 仅 `Assets/Hotfix/Runtime` 和 `Assets/Hotfix/Content` |
| 可见效果 | 蓝色徽章、公告 V1 | 橙色徽章、公告 V2 |

正式脚本要求 Git 工作区干净且 HEAD 恰好带对应标签。它会生成不可覆盖的 `Build/Releases/<release>/release.json`，记录 Git SHA、Unity/协议版本、服务器地址和关键文件 SHA256。

V1 素材和 revision 模板位于 `docs/hotupdate-demo`。基线提交完成后，只替换同地址徽章和 `HotUpdateDemoRevision.cs`，形成 V2 补丁提交。不得在补丁提交中修改 Framework、场景、ProjectSettings 或 AOT UI。

## 2. 私人局域网准备

在服务器电脑连接自己的热点／路由器后，以管理员 PowerShell 执行：

```powershell
.\scripts\setup-lan-hotupdate.ps1 -DiscoverOnly
.\scripts\setup-lan-hotupdate.ps1 -SetPrivateProfile -ExpectedNetworkName '<确认过的网络名称>' -EnableGameplay -RemediateLegacyRules
```

第二条命令会精确删除名为 `havencamp` 的旧宽泛规则，并只创建：

- Private + LocalSubnet + TCP 5080
- Private + LocalSubnet + UDP 7770

记下脚本显示的服务器 IPv4，并在路由器中保持该地址稳定。切勿把公共网络强制改成 Private。

## 3. 构建 V1 Hosted 首包

在 V1 基线提交和标签上执行：

```powershell
.\scripts\build-lan-release.ps1 `
  -ReleaseKind Baseline `
  -ContentVersion '0.1.0-baseline.1' `
  -GitTag 'demo-client-v0.1.0' `
  -ServerAddress '<服务器私人 IPv4>'
```

脚本执行 Generate All、Hosted Client、Dedicated Server、压缩包和 `release.json`。缺少显式版本、使用回环／公网地址、网络不是 Private、工作区不干净或标签不匹配时会停止。

## 4. 发布 V2 补丁

提交橙色徽章与 V2 Hotfix 逻辑并标记 `demo-patch-v0.1.0-hotfix.1`，然后执行：

```powershell
.\scripts\build-lan-release.ps1 `
  -ReleaseKind Patch `
  -ContentVersion '0.1.0-hotfix.1' `
  -GitTag 'demo-patch-v0.1.0-hotfix.1' `
  -BaselineTag 'demo-client-v0.1.0' `
  -ServerAddress '<服务器私人 IPv4>'
```

脚本会检查从基线标签起只修改了 Hotfix 代码／内容，编译 `Haven.Hotfix.dll`、发布资源并最后切换 `DefaultPackage.version`。旧 Manifest、哈希文件和 `.version.previous` 会保留。

## 5. 服务器启动与合作伙伴预检

服务器电脑打开两个终端：

```powershell
.\scripts\start-gateway.ps1 -ListenAddress 0.0.0.0 -Port 5080
.\scripts\start-lan-dedicated-server.ps1
```

合作伙伴电脑先执行：

```powershell
.\scripts\test-lan-hotupdate.ps1 -ServerAddress '<服务器私人 IPv4>' -Port 5080 -AppVersion '0.1.0'
```

服务器电脑应看到 TCP 5080 和 UDP 7770 的监听；合作伙伴预检必须取得 `/health` 和 `DefaultPackage.version`。

## 6. 三条 Player 路径

1. **无更新**：Gateway 指针保持 `0.1.0-baseline.1`，在干净的合作伙伴电脑启动 V1 客户端；下载数应为 0，直接进入大厅并显示蓝色徽章、公告 V1。
2. **失败与重试**：发布 V2 后，以 `-SimulatePatchDownloadFailure` 启动 Gateway。客户端可以取得版本和 Manifest，但 `.rawfile/.bundle` 持续返回 503；自动重试耗尽后应停留在 AOT 更新界面并显示 `HU_DOWNLOAD_FAILED`。
3. **恢复成功**：关闭故障 Gateway，以正常参数重启；不要退出客户端，点击“重试下载”。界面应显示文件数、MiB 进度，完成后显示橙色徽章、公告 V2 和 `0.1.0-hotfix.1`。

故障模式命令：

```powershell
.\scripts\start-gateway.ps1 -ListenAddress 0.0.0.0 -Port 5080 -SimulatePatchDownloadFailure
```

第二台客户端必须使用相同的 V1 压缩包重复更新。对比更新前后的客户端 ZIP、`HavenClient.exe` 和 `GameAssembly.dll` SHA256；它们必须与基线 `release.json` 完全一致。

## 7. 证据记录

每次正式验收复制本文件为带日期的记录并填写：

| 证据 | 结果 |
| --- | --- |
| 网络名称、类别、服务器 IPv4 | 待填写 |
| 精确防火墙规则导出 | 待填写 |
| TCP 5080 / UDP 7770 监听 | 待填写 |
| V1/V2 `release.json` 路径和 SHA256 | 待填写 |
| 客户端 A 无更新日志／截图 | 待填写 |
| 客户端 A 失败与重试日志／截图 | 待填写 |
| 客户端 B 补丁下载日志／截图 | 待填写 |
| V1/V2 公告和徽章截图 | 待填写 |
| 双机进入同一房间／WorldGenMap | 待填写 |
| 停服后端口不再监听 | 待填写 |

没有实际日志和截图的项目必须保持“待填写”，不得作为已验收报告。
