# MdExplorer

跨平台本地文件资源管理器，目标平台 Windows / Linux / Android，技术基线 Avalonia 12+ 与 .NET 10。

## 第一版 Avalonia 桌面应用

`src/MdExplorer.Desktop` 是原生 C# / Avalonia 桌面应用，使用 `Md3.Avalonia` 的 `MaterialTheme`、`MdButton`、`MdTextBox` 与 `MdCard` 控件，支持系统跟随/浅色/深色主题循环切换，并将偏好保存到用户配置目录。当前首版提供本地目录浏览、按名称搜索、名称/修改时间/大小/类型排序、隐藏文件开关、文件/文件夹选择、文本与图片预览、文件信息、新建文件夹、重命名、复制/剪切/粘贴（当前单项）、永久删除、系统打开文件，以及预览/左右双栏/上下双栏工作区切换。

```bash
dotnet restore MdExplorer.sln
dotnet run --project src/MdExplorer.Desktop
```

GitHub Actions (`.github/workflows/build.yml`) 在 Ubuntu 和 Windows 上 restore/build 验证桌面项目。Linux 桌面运行还需安装发行版对应的 Avalonia 原生依赖。Android Host/SAF 尚未并入第一版，此版不能视为 Android 可发布应用。

## 设计材料

- [开发设计文档](docs/development-design.md)：分层架构、平台抽象、AOT 与依赖治理、功能规划。
- [多尺寸布局设计图](docs/layout-design.svg)：手机、平板、桌面与双栏模式示意。
- [交互式 HTML 参考原型](prototype/index.html)：用于设计对照；采用演示数据，不访问真实文件。

该首版是可工作的桌面 MVP，平台抽象、Android SAF、回收站、完整多选/复制移动及专业 PDF 预览仍待后续迭代。