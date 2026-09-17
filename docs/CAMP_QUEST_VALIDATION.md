# 营地委托验证记录

验证日期：2026-09-16 至 2026-09-17。工作分支：`feature/camp-quests`；集成基线：`56523d0`。实现仍为本地未提交变更，不把基线提交号当作交付二进制的源码版本。

环境：Windows，Unity 6000.5.5f1，现有锁定 HybridCLR 8.14.1 / YooAsset 3.0.5，.NET 8。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 原项目基线 Unity 编译 | 通过 | `Logs/CampBaseline.log` |
| 营地场景生成和 Missing Script 检查 | 通过 | `Logs/CampSetup3.log` |
| Unity EditMode（含 EditorSimulate 资源加载回归） | 25/25 通过 | `Logs/CampEditMode3Results.xml` |
| EditorDirect 地图任务烟测 | 两次交付、真实拾取、准确扣料奖励、存档失败回滚、场景重载通过 | `Logs/CampEditorSmoke3.log` |
| 真实背包、奖励分栈、原子存档 | 10/10 检查通过 | `Logs/CampBuild2.log` 中 `CAMP_ADAPTER_PASS` |
| Gateway 单元测试 | 30/30 通过 | `Logs/GatewayTests/CampGatewayFinal.trx` |
| 本机 HTTP，合法请求但无 Key | 返回 503，可供客户端回退 | 实际启动 Gateway 的 HTTP 检查 |
| 本机 HTTP，篡改奖励为 999 | 返回 400，调用模型前拒绝 | 实际启动 Gateway 的 HTTP 检查 |
| 本机 HTTP，模拟模型成功响应 | 通过；本机样本总耗时 672 ms，仅用于连通性检查 | `scripts/tests/camp-model-fixture.mjs`；不代表真实 DeepSeek 延迟 |
| HybridCLR Generate All / Windows IL2CPP | 修复版完整构建通过，资源版本 `0.1.0-20260916155302962` | `Logs/CampBuild3.log`；`Build/WindowsCampQuest/HavenCamp.exe` |
| Windows EXE：离线任务、准确扣料、奖励、存档回滚 | 通过，关闭 Gateway 的本地委托完成两次交付 | `Logs/CampPlayerSmoke2.log` |
| Windows EXE：进程内场景重载、退出后再次读档 | 两种读档方式均通过，额度和贡献不丢失 | `Logs/CampPlayerSmoke2.log`；`Logs/CampPlayerReload2.log` |
| Windows EXE：客户端经 Gateway 的成功路径 | 通过，使用本机模拟模型返回候选；不等于真实 DeepSeek 验收 | `Logs/CampPlayerMockOnline.log`；`scripts/tests/camp-model-fixture.mjs` |
| 随包 Gateway 启动和 `/health` | 通过；Windows PowerShell 5.1 启动脚本已验证，仅监听 `127.0.0.1:5080` | `Build/WindowsCampQuest/start-camp-gateway.ps1`；实际 HTTP 响应 |
| Framework 配置校验 | 通过 | `Logs/CampValidateFinal.log` |
| 真实 DeepSeek 账号请求与实际延迟 | 通过；一条独立 HTTP 样本端到端 1,449 ms，返回合法 `fire_preparation_small` 和中文对白 | 用户本机 Gateway；合成世界状态，不含玩家真实数据；这是单次样本 |
| Windows EXE：真实 DeepSeek 任务闭环 | 通过；在线生成 `reserve_wood`、`reserve_rock` 两项委托，交付、奖励和重载防重复领奖均成功 | `Logs/CampPlayerRealDeepSeek.log` |
| 人工鼠标操作、中文显示、长时间游玩与最终手感 | 待人工验收 | 隐藏窗口无法生成 Unity 截图；自动化烟测不能代替此项 |

已发现并修复：

1. 原项目缺少 TMP Essential Resources，导致中文字体材质创建失败；导入官方资源并制作 Noto 动态字库。
2. JsonUtility 会将空的内联任务对象还原为默认对象；存档使用显式存在标记，重跑任务测试通过。
3. 编辑器原来停留在 Server 子目标，热更 DLL 被编译成 .NET Standard，而 Player AOT 为 .NET Framework，导致 HybridCLR 泛型分析失败。独立构建入口在生成前临时切换 Player，最后恢复原子目标。
4. 首次 Windows 启动发现原热更加载器只支持 RawFileObject，但 LegacyBuildPipeline 产出的是包含 TextAsset 的 AssetBundle；加载器现支持两种表示，同时避免 SafeCoroutine 自动释放借用的 YooAsset 句柄。新增 EditorSimulate 资源加载回归测试和修复版 Windows 运行均已通过。首次启动从 YooAsset 加载 `Haven.Hotfix`；场景重载显示“已加载”，构建时该程序集被 HybridCLR 从主程序集过滤。
5. Windows PowerShell 5.1 会把不带 BOM 的 UTF-8 脚本中的中文提示按旧编码解码，导致 `start-camp-gateway.ps1` 解析失败。启动和打包脚本的控制台提示改为 ASCII；已用 PowerShell 5.1 解析并实际启动验证。没有读取或存储用户 API Key。

原素材还存在跨 CraftData 的 `grass` ID 重复日志（物品与旧植物配置同名）；目前保留其原 ID，避免未经迁移破坏已有存档。本次委托使用的 wood / rock / bread 配置不涉及该重复项。

测试构造了错误返回、无 Key、空内容、非法 JSON、未知候选、奖励篡改、超时、容量不足、跨背包扣除、重复交付、放弃、跨日、迟延结果和损坏任务状态。自动化存档检查只操作专用随机文件，锁定文件期间确认旧存档仍可读取；测试文件随后清理，不删除普通玩家存档。

日志和演示输出被 Git 忽略，应在本机复查。`ScreenCapture.CaptureScreenshot` 在隐藏／批处理窗口中报告失败，没有产出截图；可见窗口的人工验收时补截图。一次无 `-batchmode` 的隐藏窗口测试在完成逻辑检查并退出时返回 `0xC0000005`，因此交付验收还要检查普通可见窗口的退出行为。

## 管事聊天增量验证（2026-09-17）

新增 `POST /api/v1/camp-npc/chat`、聊天页、Hotfix 会话服务与离线对白；现有委托规则未交给模型。Gateway 测试 45/45 通过；Unity EditMode 32/32 通过。用 `scripts/tests/camp-model-fixture.mjs` 在本机 5091 模拟模型、Gateway 在 5092 运行，HTTP 聊天请求收到合法中文回复；这是模拟测试，不是真实 DeepSeek 调用。EditorDirect 地图烟测通过聊天页打开／返回、模拟在线回复、两次真实拾取交付、场景重载后聊天记录清空及任务记录保留，日志见 `Build/Logs/CampChatEditorSmoke.log`。`HAVEN_CAMP_GATEWAY_PORT=5092` 仅用于本机模拟测试。

新增 Framework 契约后的 Windows Player 已重新执行 HybridCLR Generate All、Offline 内容构建及 Windows IL2CPP 构建，退出码 0，日志 `Build/Logs/CampChatBuild.log`。新资源版本 `0.1.0-20260917150805492`；用 `scripts/package-camp-demo.ps1` 已重新发布随包 Gateway、启动脚本及说明文档。

新 Windows EXE 的本机模拟在线烟测通过：聊天为 `source=deepseek`（由本机模型模拟器提供，不是真实 DeepSeek），两项委托、奖励与重载仍通过，见 `Build/Logs/CampChatPlayerMock.log`。完全不连接 Gateway 的烟测亦通过：聊天和两项委托均使用 `local-fallback`，两次交付及重载成功，见 `Build/Logs/CampChatPlayerOffline.log`。两次运行均使用专门的 `HavenCamp_AutomatedSmoke` 测试槽位，退出码 0。

随包发布的 Gateway 已在本机 `5093` 独立启动并通过 `/health` 检查；无 Key 时新聊天路由返回 503，客户端按既有逻辑本地回退。测试用的 5091、5092、5093 服务已关闭，未触碰用户自己的 Gateway。

真实 DeepSeek 的**聊天**仍未验收：用户曾重启 Gateway，但本机检查 `127.0.0.1:5080` 先返回 `deepSeekConfigured: false`，再次配置后连接被拒绝；需要查看其终端启动输出并在确认 Key 仅存进程环境后重试。此前真实 DeepSeek **委托**验证独立有效，不能据此推断新聊天接口已通过。

随包资源版本：`0.1.0-20260917150805492`。演示包约 2.94 GB，须完整复制；`GameAssembly.dll` 的 SHA256 为 `75B113E684DC47331D783A6D7324203ACD2EB586628F19A6551260D249135290`。这两项对应本地当前构建，修改文件后应重新计算。

## 管事站位修正（2026-09-18）

`WorldGenMap` 原管事位置 `(3.10, 0.05, 0.80)` 距最近松树约 1.2 米，树冠遮住人物。已移到 `(-3.00, 0.05, -1.00)` 并面向玩家；场景初始化默认位置同步调整。Unity 场景校验通过，日志 `Build/Logs/CampStewardReposition.log`。EditorDirect 离线烟测通过点击交互、聊天回退、两次物资交付和场景重载，退出码 0，日志 `Build/Logs/CampStewardMovedSmoke.log`。先前 Windows 演示包未因本次场景调整重新构建，仍包含旧站位。
