# Haven 营地物资委托

本功能是 `WorldGenMap` 的**单机**玩法，不是 FishNet 服务端权威任务系统。AI 只选择合法候选并生成理由；物资数量、面包奖励、扣料、结算和存档由本地游戏规则决定。原 `FrameworkDemo` 联机原型与旧 Gateway 接口保留。

## 运行和演示

1. Unity 使用 `6000.5.5f1`，打开 `Assets/Scenes/WorldGenMap.unity`，保持 `EditorDirect`，进入 Play。
2. 点击出生点附近标有“营地管事”的人物；角色走近后打开面板。按“轻松一些”，再点“询问营地需求”。
   面板右上方的“聊聊营地”可切换到聊天页，输入最多 120 字并发送；“询问委托”返回任务页。
3. 接受委托，关闭面板，点击附近木材/石料堆，用原游戏交互拾取。右上角追踪显示随身持有量。材料也可以由原地图的采集操作获得。
4. 返回管事处，交付全部物资，获得可按原物品操作食用的面包。存档成功才确认完成。交付的物资只计入贡献，不自动造墙或给篝火添燃料。
5. 继续完成另一类物资委托；同一游戏日木材、石料各一次。同时只允许一项任务。
6. 使用原暂停菜单（Esc）的 Save / Load / New 功能。接取、放弃、生成提议和交付也会保存。退出重进后完成记录保留，已完成任务不能再次领奖。New 会重置当前槽位，先备份需要保留的存档。
7. 关闭 Gateway，在新存档或次日再次询问；应显示“已使用营地本地委托”，后续采集、交付照常。

独立演示包：双击 `Build/WindowsCampQuest/HavenCamp.exe`。游戏本身不需要 .NET 或 Gateway；可选 Gateway 需要 **ASP.NET Core Runtime 8** 或 **.NET 8 SDK**（仅安装普通 .NET Runtime 不够）。分发**整个文件夹**，不要只复制 EXE。此包使用内置 YooAsset 内容的 Offline 模式，并实际加载 HybridCLR 热更 DLL；不依赖补丁服务器启动。

## 可选：真实 DeepSeek

Key 仅放在启动 Gateway 的 PowerShell 进程中，不写进 Unity 资源、脚本、Git 或演示包。推荐在单独的 PowerShell 窗口安全输入：

```powershell
$questKey = Read-Host 'DeepSeek API Key' -AsSecureString
$questKeyPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($questKey)
try { $env:DeepSeek__ApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($questKeyPtr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($questKeyPtr) }
$env:DeepSeek__Model = 'deepseek-flash'
.\scripts\start-camp-gateway.ps1
```

演示包内使用 `./start-camp-gateway.ps1`（没有 scripts 前缀）。客户端从资源管理器或另一个没有 Key 的终端启动。检查 `http://127.0.0.1:5080/health` 的 `deepSeekConfigured`。不需要开放防火墙，不启动 Dedicated Server。若本地其他程序占用 5080，应先确认并自行停止那个程序。

客户端默认只请求本机 `5080`；测试时可在启动客户端的进程环境中设置 `HAVEN_CAMP_GATEWAY_PORT` 指向另一台本机测试 Gateway 端口，不接受外部主机地址。

如原 Gateway 配置了 `Gateway__SharedToken`，客户端环境中的 `HAVEN_GATEWAY_TOKEN` 必须一致；这不是 DeepSeek Key。普通本机演示可不配置共享令牌。不要将此无鉴权的单机配置直接暴露到公网。

默认模型 `deepseek-flash`、`thinking.type=disabled`、`response_format.type=json_object`。模型名称可用 `DeepSeek__Model` 覆盖；提示词版本 `camp-quests-1`。配置依据 [DeepSeek 模型说明](https://api-docs.deepseek.com/quick_start/pricing/) 与 [JSON 输出说明](https://api-docs.deepseek.com/guides/json_mode/)。模型服务和别名会变化，应以实际账号可用模型为准。

Gateway 单次总预算 8 秒，客户端 10 秒，不自动重试。无 Key、断网、HTTP 错误、超时、空内容、非法 JSON 或未知候选均由客户端转为确定性本地候选。重新打开面板不会再次计费；已有提议保留到接取、放弃或跨日。

聊天走独立的 `POST /api/v1/camp-npc/chat`，使用同一个本机 Gateway、DeepSeek Key 和模型，聊天提示词版本为 `camp-npc-chat-1`。每次在线发送都会产生一次模型请求；没有 Key、超时或服务不可用时改为“本地回复”。一次只发一条，最多保留最近六组问答，发送给模型的是最近六条消息。聊天记录仅在当前场景／存档会话的内存中，重载场景或切换存档后清空，不写入游戏存档。AI 只生成营地对白，不会执行发物品、建造或结算任务；输出会经过 Gateway 和游戏两次检查。旧版已运行的 Gateway 没有聊天路由，须先停止再重启新源码才能在线聊天。

在线聊天会将输入文字、最近六条对话与营地状态发送给 DeepSeek；不要在聊天框输入真实姓名、账号、密码、API Key 等个人或保密信息。Gateway 日志仅记录请求 ID、模型、提示词版本和耗时，不记录对话正文。

在线成功时 Gateway 记录 `requestId / model / prompt / elapsedMs`，不记录 Key 或玩家输入；客户端提示“管事为你准备了一项委托”。自动烟测另有 `proposal source=deepseek/local-fallback` 日志。真实延迟必须来自实际日志，模拟响应不代表 DeepSeek 实际效果。

## 规则与边界

| 条件 | 基础档 | 加量档 |
| --- | --- | --- |
| 25 米内已有篝火，16:00 至次日 06:00 | 木材 3 → 面包 1 | 木材 5 → 面包 2 |
| 25 米内没有已建成篝火 | 石料 2 → 面包 1 | 石料 4 → 面包 2 |
| 25 米内已建成木墙不足 2 段 | 木材 3 → 面包 1 | 木材 5 → 面包 2 |
| 对应物资当天未交付（日常储备） | 木材 3 或石料 2 → 面包 1 | 无 |

仅统计主背包和随身袋，不读取装备或远处箱子；持有量会随制作、丢弃而减少。交付距离不超过 3 米，不支持分批。接取后条件、数量、奖励固定且跨夜有效；额度按**实际交付日**记录。放弃不扣料。模型输出文字不驱动任务行为，UI 数量来自配置。

目录唯一源：`Assets/Hotfix/Content/CampQuestCatalog.json`；Gateway 编译时复制同一文件。修改后应同时重建游戏内容和 Gateway。提议在显示和接受前再次校验，Gateway 对客户端候选与本地目录逐字段比较。

## 架构与存档

- `Haven.Framework / Runtime/CampQuests`：稳定 DTO 与委托、聊天服务接口。
- `Haven.Hotfix / Runtime/CampQuests`：候选规则、任务状态机、聊天会话和本地回退；`CampQuestModule` 仅在场景安装世界适配器时启用。
- `Assets/HavenCamp/Runtime`（Assembly-CSharp）：旧 SurvivalEngine 的世界/背包适配、uGUI/TMP 面板、Selectable 管事和 Gateway HTTP 适配。
- `POST /api/v1/camp-quests/propose`：委托接口；`POST /api/v1/camp-npc/chat`：聊天接口；`POST /api/v1/quests/generate` 不变。
- 本场景使用随场景销毁的 GameBootstrap；原菜单重新加载场景时重建服务，取消旧异步请求。

存档键为 `PlayerData.unique_strings["haven.campQuests.v1"]`。显式存在标记避免 JsonUtility 将空任务还原成默认对象；旧存档无字段时初始化为空，有损坏/未知版本时禁止结算，不默默抹掉记录。

结算先克隆主背包/随身袋并模拟扣料、奖励容量，再在同一主线程操作中替换库存、写任务记录和保存世界。写盘采用同目录临时文件 + `File.Replace`，上一份文件保留 `.bak`；失败回滚本次库存和任务记录。现有 BinaryFormatter 格式仅为兼容旧存档保留：**只加载自己的可信本地存档**，不接收网络传来的未知存档文件。

中文使用 Noto Sans CJK SC，许可见 `Assets/HavenCamp/Fonts/OFL.txt`。管事只复用现有人形显示资源，不带旧玩家控制脚本。

## 构建与验证

首次克隆后安装 Windows IL2CPP 模块及 HybridCLR Runtime。TMP Essential Resources 已在仓库中；若缺失，使用 `Window > TextMeshPro > Import TMP Essential Resources`。

- `Haven/Camp Quests/1. Prepare WorldGenMap`：幂等配置场景，不重新生成原地图。
- `Haven/Camp Quests/2. Validate Scene`：检查管事、UI、物品和 Missing Script。
- `Haven/Camp Quests/3. Run Adapter Checks`：真实库存与原子存档检查。
- `Haven/Build/Windows Camp Quest Demo`：Generate All → 制作内置内容 → IL2CPP Windows Player。结束后恢复开发模式。新增稳定契约必须重发完整客户端，不能仅替换旧 EXE 的热更 DLL。

Player 构建后运行 `./scripts/package-camp-demo.ps1`，将可选 Gateway、启动脚本、本文、验证记录和字体许可放入演示包。

```powershell
dotnet test .\Server\Haven.Gateway.Tests\Haven.Gateway.Tests.csproj --configuration Release

# 单独的自动化测试槽位；不会操作普通 player 存档
.\Build\WindowsCampQuest\HavenCamp.exe -campQuestSmoke -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$PWD\Logs\CampPlayerSmoke.log"
.\Build\WindowsCampQuest\HavenCamp.exe -campQuestReloadSmoke -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$PWD\Logs\CampPlayerReload.log"
```

Unity EditMode 用 Test Runner 运行 `Haven.Framework.Tests`。烟测会使用真实拾取接口和真实物品，完成两项任务并检查扣料/奖励、故意锁住测试存档验证回滚、重复交付、再次启动后的完成记录。截图请求会写到游戏 `Application.persistentDataPath`，命名 `CampQuestProposal.png` 和 `CampQuestCompleted.png`；批处理或隐藏窗口可能无法截图，请在可见窗口人工补拍。自动化测试使用位移辅助，不替代人工的鼠标路径与手感验收。

测试记录见 `docs/CAMP_QUEST_VALIDATION.md`；未执行的项目必须按未验收处理。`Build/`、`Logs/`、Gateway `bin/obj` 均不提交 Git。
