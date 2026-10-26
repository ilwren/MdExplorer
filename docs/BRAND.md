# Eidoria（万象屿）品牌与图标规范

## 1. 名称

产品正式显示名为 **Eidoria（万象屿）**，读作 *eye-DOR-ee-a*。`MdExplorer` 继续保留为内部工程代号。

- **Eidoria**：由希腊语 *eidos*（形相、形象、观念）与带有“境域 / 收藏之地”意味的 *-oria* 拼合而成，意为“万象栖居之地”。它不直译“文件管理器”，而是把本地文件看作照片、文档、代码、声音与记忆的万千形相。
- **万象屿**：中文名取“万象”与“屿”。文件系统像一座由路径相连的岛屿，文件夹是地层，文件树向上生长，预览则让内容从岛上浮现。
- 品牌语气：安静、策展式、带有自然史标本馆与小型美术馆的气质；避免企业软件式的功能直译命名。

本轮曾淘汰第一版显示名 Layerfold：语义准确但过于字面，缺少独立世界观。其他候选如 Folio、Materio、ChromaFold、Papyra、Treefold、Sylvaris、Velluna、Scrinium、Herbarium、Foliora 与 Vellichor 均已有软件或相近产品使用。`Eidoria` 在本轮公开网页浅层排查中未检到同名软件产品；正式发布前仍应做商标、应用商店与域名清查。

## 2. 图标构造

主稿：`docs/assets/brand/eidoria-icon.svg`（1024×1024）。

- **三层纸页拱门**：珊瑚、琥珀与墨青色拱门以轻微错版印刷的方式叠置，既是美术馆入口、打开的书页，也是文件夹由外向内的层级。
- **金日**：中央圆日代表被预览照亮的内容，也是岛屿上空的太阳/满月。
- **分枝路径**：乳白色枝条把文件树转译为向上舒展的蕨叶；节点不再是工程图符号，而是新芽与星点。
- **地层页浪**：下方三层青绿色与珊瑚色带同时像岛屿地层、海浪和摊开的纸页，承接“万象屿”的地貌。
- **书册基座**：底部白色弯曲页形让整座拱门像从一本打开的收藏册中升起。
- **星屑**：少量四角星与圆点只作呼吸，不增加额外叙事；小尺寸下可忽略，主体仍靠拱门、金日和枝条识别。
- 外轮廓为约 22.5% 半径的 squircle；主要图形控制在画布 16%–88% 区间，Android adaptive 前景另行简化并收进安全区。
- 不使用字母、产品文字或直白文件夹造型；品牌识别来自拱门与金日的组合。

## 3. 核心色

| 角色 | Hex | 说明 |
|---|---:|---|
| Twilight ink | `#123D5C` | 图标底与拱门内腔的基础深色 |
| Deep teal | `#006A6A` | 与应用 MD3 默认种子色保持连续 |
| Coral paper | `#FF7A66` | 最外层纸页 / 暖色错版影 |
| Amber paper | `#F59E5B` | 中层拱门与日光过渡 |
| Sun gold | `#FFC94D` | 中央圆日 |
| Lagoon | `#159E93` | 地层主带 |
| Sea glass | `#9FE8D2` | 内拱线与浅色地层 |
| Warm paper | `#FFF5CF` | 分枝路径与高光 |

## 4. 资产落点

| 用途 | 文件 |
|---|---|
| 可编辑主稿 | `docs/assets/brand/eidoria-icon.svg` |
| 品牌 PNG（1024） | `docs/assets/brand/eidoria-icon.png` |
| UI 内嵌 PNG（512） | `src/MdExplorer.UI/Assets/eidoria.png` |
| 窗口/桌面 ICO | `src/MdExplorer.UI/Assets/eidoria.ico`（16/20/24/32/40/48/64/128/256） |
| Android adaptive | `src/MdExplorer.Android/Resources/mipmap-anydpi-v26/ic_launcher.xml` + `drawable/ic_launcher_foreground.xml` |
| Android 旧矢量兜底 | `src/MdExplorer.Android/Resources/drawable/icon.xml` |

修改主稿后须重新导出 PNG/ICO，并同步检查 Android adaptive 的简化前景、圆形裁切与小尺寸识别。

## 5. 使用规则

- 不拉伸、不旋转、不给图标加描边、投影或外发光，不在图标内叠加产品文字。
- 不把分枝路径单独抽出冒充分享、同步等功能图标；界面功能图标继续使用 Material Symbols。
- 图标放在浅色背景上时保留自身暮色底，不额外垫白色卡片。
- 品牌名在窗口、启动器等窄空间只显示 `Eidoria`；抽屉、README 与关于页可同时显示“万象屿”。

## 6. 改名迁移边界（v0.6）

本轮仍只改**产品品牌层**：窗口标题、抽屉品牌、设置页名称、Android 应用名、图标、桌面产品元数据与文档标题。

以下内部标识暂不改，避免升级与数据迁移风险：

- GitHub 仓库名、解决方案/项目目录与 C# 命名空间：仍为 `MdExplorer`；
- Android `ApplicationId=dev.mdexplorer.app`：保持应用更新身份连续；
- 桌面 `AssemblyName=MdExplorer` 与本地数据目录 `MdExplorer`：保持已有设置、收藏与快捷方式可继续使用；
- 应用内拖放私有数据格式仍为 `x-mdexplorer-paths`。

若 1.0 前进行仓库级改名，应单独立项处理重定向、数据目录迁移与发布兼容。
