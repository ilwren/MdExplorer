# 本地 AI 接入计划与可行性评估

状态：**评估完成，分期路线已定**（2026-10-26），待维护者确认 D-08/D-09 后按期推进。范围：全部**本地推理**（无云端 API、无遥测），与既定的"纯本地文件管理"定位一致。模型与运行时的许可、体积、中文可用性、AOT 与三平台可行性逐项核过（证据链接见 §9）。插件侧的通道复用见 `docs/PLUGIN-SYSTEM.md` §5/P2。

## 1. 结论：哪些能做、什么顺序

| 场景 | 可行性 | 主路线 | 关键模型与许可 | 体积（按需下载） | 分期 |
|---|---|---|---|---|---|
| **文本朗读（TTS）** 文本预览"朗读" | ✅ 高 | OS 原生 → sherpa-onnx | 系统 TTS（0 增量）；Kokoro 多语 EN+ZH（Apache-2.0） | 0 / ≈90 MB（int8）起 | **A1**（含 OS 原生） |
| **多模态语义搜图** "海边的落日" | ✅ 高 | ONNX Runtime + SQLite 向量索引 | SigLIP 2 Base（Google，Apache-2.0，多语）；备选 Chinese-CLIP（代码 MIT、权重逐个核） | ≈100–200 MB（int8 双塔） | **A2** MVP 场景 |
| **识图：自动标签 + 说明** | ✅ 中高 | ONNX（Florence-2-base，0.23B，MIT） | 输出英文为主 → 主要喂搜索索引，UI 少量呈现 | ≈250 MB（int8） | **A3** |
| **文档语义搜索** （txt/md 等正文） | ✅ 中 | 文本嵌入 + 同一向量索引 | 多语小型嵌入（许可逐个核，如 bge-small 系 MIT） | ≈50–120 MB | **A3** 顺带 |
| **语音转写（ASR）** 音频预览出字幕、可搜 | ✅ 中 | sherpa-onnx / whisper.cpp（均 MIT 系） | Whisper tiny/base（MIT）中英可用 | ≈75–150 MB | **A4** |
| **本地多模态问答（VLM）** "这张图里有什么" | △ 中（体积/速度） | llama.cpp（MIT，经 LLamaSharp MIT） | 模型许可逐个核（优先 Apache-2.0 系权重）；数 GB 级 | ≥1.5 GB | **A4**，且仅桌面 |
| 智能重命名建议、相似图分组 | ✅ 派生 | 复用 A2/A3 的向量与标签 | — | 0 增量 | A3 后派生 |

相对价值排序的依据：**TTS（OS 原生）零成本先落地；语义搜图是文件管理器差异化主菜且技术链最成熟（ONNX 双塔编码+余弦），排 A2；识图主要做索引燃料（A3）而非聊天秀；VLM 问答体积 GB 级、桌面亦需耐心，排最后且设为可选。**

## 2. 场景与产品形态

- **A1 朗读**：文本/代码预览区增加"朗读/暂停/继续"；长按或选择模式朗读所选文件。无障碍价值明确（§12 指标体系的延伸）。实现：`ITextToSpeechService` 端口 → Windows（WinRT SpeechSynthesizer)/Linux(speech-dispatcher；缺席即隐藏）/Android（TextToSpeech）先行；下载模型版随后（sherpa-onnx 一套覆盖 TTS+ASR)。
- **A2 语义搜图**：搜索框增设模式切换（文件名 / 语义）。语义模式对已选"索引根"内的图片做图文向量匹配，结果复用现有浏览器网格（相似度排序 + 分数徽标）。索引为空时引导去设置页"建立照片语义索引（需下载约 150 MB 模型）"。SigLIP 2 的设计即图文同空间嵌入，Elasticsearch 与多个本地优先项目已用它做照片文本检索，模式成熟（[Elastic 实践](https://www.elastic.co/search-labs/blog/multimodal-search-siglip-2-elasticsearch)、[Tauri+ONNX+SQLite 本地实现参考](https://dev.to/ishannaik/local-first-semantic-search-on-windows-running-siglip2-and-dinov3-with-tauri-2-and-onnx-19f7)）。
- **A3 识图**：预览侧栏多一张"AI 识别"卡（标签 chips + 一句话说明，点标签即按标签筛）；标签与说明默认**只进索引库**（不写文件/不改 EXIF，除非用户在设置打开"写入备注"未来项）。文档语义搜索复用管线：正文分块嵌入，与文件名搜索分开入口。
- **A4 转写与问答**：音频预览生成字幕条（滚动对齐播放为 D-03 播放功能就绪后事，先出全文转写+导出 .srt)；VLM 问答为预览侧栏可选实验卡（桌面、显式下载、可关闭）。

## 3. 运行时选型

| 运行时 | 许可 | AOT | 三平台 | 角色 |
|---|---|---|---|---|
| **ONNX Runtime**（Microsoft.ML.OnnxRuntime 1.31 线） | MIT | ⚠️ 可行非一等：1.15+ 有 Native AOT 支持路径（[部署实测](https://sarikayadev.com/en-us/blog/running-ai-inference-on-net-8-native-aot-for-edge-iot-devices-with-onnx-runtime/)），但移动 RID 静态链接与修剪根需自理、无官方"Na
...[truncated 5597 chars]