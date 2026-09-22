# 热更新徽章发布素材

- `HotUpdateDemoBadge-v1.png` 是 Hosted 首包基线使用的蓝色徽章。
- 当前 `Assets/Hotfix/Content/HotUpdateDemo/HotUpdateDemoBadge.png` 是补丁使用的橙色 V2 徽章。
- V1 构建前，将蓝色图片复制到上述热更新地址，并用 `HotUpdateDemoRevision-v1.cs.txt` 的内容替换 `HotUpdateDemoRevision.cs`；提交并标记 `demo-client-v0.1.0`。
- V2 补丁提交只恢复橙色图片和 V2 revision 文件，标记 `demo-patch-v0.1.0-hotfix.1`。这样同一资源地址和同一 Hotfix 类型会产生真实的资源包、DLL 差异。

两张徽章均通过内置图像生成工具生成，要求透明背景、无文字、适合 Unity Sprite；V1 为蓝色，V2 为橙色。Unity 的正式资源仅保留当前版本，基线素材位于本目录，避免被 YooAsset 同时收集。
