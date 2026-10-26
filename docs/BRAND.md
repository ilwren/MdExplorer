# Layerfold 品牌与图标规范

## 1. 名称

产品正式显示名改为 **Layerfold（层夹）**，原 `MdExplorer` 保留为内部工程代号。

- **Layer**：Material Design 以层叠表面组织信息层级；应用本身也以抽屉、文件区、预览层构成空间关系。
- **Fold**：同时指向 folder（文件夹）与 folded paper（折纸/纸面），把“文件夹”与 Material 的纸张隐喻合在一起。
- **层夹**：中文为“层叠 + 文件夹”的缩合，与英文构成一一对应的双关。

命名排查中曾考虑 `Folio`、`Materio`、`ChromaFold`、`Papyra`、`Treefold`，均已有明显软件或同类产品占用；`Material Files` 也已有成熟同类应用，因此采用较具独创性的合成词 `Layerfold`。

## 2. 图标构造

主稿：`docs/assets/brand/layerfold-icon.svg`（1024×1024）。

- 外轮廓：圆角 squircle，半径为画布约 22.5%，便于桌面与启动器裁切。
- 主体：三层逐级下沉的文件夹前面板，表达 Layer；后层有传统文件夹页签。
- 纸张：两张错开的纸页从文件夹中探出，右上折角表达 Fold 与内容预览。
- 标记：前景中央的分支节点代表文件树、路径与探索，不使用字母，避免语言绑定。
- 风格：Material You 的 tonal 分层、扁平大色块、轻量纵深；不做拟物纹理，不加文字。
- 安全区：主要图形控制在画布 16%–86% 区间；Android adaptive 前景进一步收至约 20%–81% 区间。
- 最小尺寸：完整图标建议 ≥24px；16px 任务栏尺寸由 ICO 中的专用小尺寸条目承担。

## 3. 核心色

| 角色 | Hex | 说明 |
|---|---:|---|
| Seed / Primary | `#006A6A` | 与应用 MD3 默认种子色一致 |
| Deep surface | `#013B4A` | 背景深端 |
| Layer mid | `#159E93` | 文件夹中层 |
| Layer light | `#43C4B5` | 文件夹亮层 |
| Back layer | `#69D2C3` | 文件夹背板 |
| Paper | `#F4FFFB` | 主纸面 |
| Path mark | `#D8FFF6` | 文件树标记 |

## 4. 资产落点

| 用途 | 文件 |
|---|---|
| 可编辑主稿 | `docs/assets/brand/layerfold-icon.svg` |
| 品牌 PNG（1024） | `docs/assets/brand/layerfold-icon.png` |
| UI 内嵌 PNG（512） | `src/MdExplorer.UI/Assets/layerfold.png` |
| 窗口/桌面 ICO | `src/MdExplorer.UI/Assets/layerfold.ico`（16/20/24/32/40/48/64/128/256） |
| Android adaptive | `src/MdExplorer.Android/Resources/mipmap-anydpi-v26/ic_launcher.xml` + `drawable/ic_launcher_foreground.xml` |
| Android 旧矢量兜底 | `src/MdExplorer.Android/Resources/drawable/icon.xml` |

修改主稿后须重新导出 PNG/ICO 并人工检查 Android 前景是否仍在安全区内。

## 5. 使用规则

- 不拉伸、不旋转、不给图标加描边或外发光，不在图标内叠加产品文字。
- 不单独把分支标记改造成分享图标使用；界面内功能图标继续使用 Material Symbols。
- 图标放在浅色背景上时保留自身深色底，不额外垫白色卡片。

## 6. 改名迁移边界（v0.5）

本轮改的是**产品品牌层**：窗口标题、抽屉品牌、设置页名称、Android 应用名、图标、桌面产品元数据与文档标题。

以下内部标识暂不改，避免无谓的升级与数据迁移风险：

- GitHub 仓库名、解决方案/项目目录与 C# 命名空间：仍为 `MdExplorer`；
- Android `ApplicationId=dev.mdexplorer.app`：保持应用更新身份连续；
- 桌面 `AssemblyName=MdExplorer` 与本地数据目录 `MdExplorer`：保持已有设置、收藏与快捷方式可继续使用；
- 应用内拖放私有数据格式仍为 `x-mdexplorer-paths`。

待 1.0 前如需仓库改名，应单独立项处理重定向、数据目录迁移与发布兼容，不与功能批次混做。
