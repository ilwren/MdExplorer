# 本地 AI 接入计划与可行性评估

状态：**A0 模型分发已落地；A2 本地文本语义搜索已接线**（v0.38，2026-10-30）。真实下载权重后的检索质量/时延仍待设备验收。
范围：全部**本地推理**——无云端 API、无遥测、不发起任何网络请求。取模型是显式的、用户自己动手的一次性动作（`scripts/fetch-ai-models.*`），应用本体离线可用。插件侧通道见 `docs/PLUGIN-SYSTEM.md` §5。

> 这一版不吹能力：设置页只说「装没装、占多大、缺几个文件」，没有装模型时明确写「现在不可用」，不会出现按下去没反应的 AI 按钮。

## 1. 场景与顺序（图片/音频小模型评估）

| 场景 | 候选模型 | 体积 | 许可 / 语言 | 产品定位 |
|---|---|---:|---|---|
| **文本向量检索** | BGE small zh v1.5 + all-MiniLM-L6-v2 | 26 + 23 MB | MIT + Apache-2.0；中文/英文分开 | 当前默认轻量包；文件名/正文语义检索 |
| **图片语义检索** | SigLIP 2 图文双塔（图像/文本各一个编码器） | 约 100–200 MB（量化选择待核） | Apache-2.0；多语 | 对「找海边的落日」最合适，但它只产向量/相似度，不会回答「这图里几个人」 |
| **图片问答/描述/OCR 线索** | **SmolVLM-256M-Instruct，ONNX q4f16** | **≈194 MB，256M 参数** | Apache-2.0；模型卡标注 English，中文能力需实测 | 本次加为可选下载项；一套三段 ONNX（视觉编码器、词嵌入、解码器）+ tokenizer/processor，适合生成图片描述、图像问答与文档视觉线索；不是严格 OCR 引擎 |
| **音频语音转写（ASR）** | **Whisper tiny multilingual，ONNX q4f16** | **≈57 MB，39M 参数** | Apache-2.0（ONNX 导出仓库声明；上游 OpenAI 源代码 MIT）；多语含中文 | 本次加为可选下载项；对音频生成文字/字幕，做不到可靠说话人分离，也不是音乐理解模型 |
| **音频事件分类** | **YAMNet ONNX** | **≈16 MB，3.7M 参数** | Apache-2.0；AudioSet 521 类 | 本次加为可选下载项；给出「音乐/说话/犬吠/警笛」一类标签，不输出转写文字 |
| **更小的纯语音选项** | Moonshine tiny | ≈27M 参数 | 主要英语，模型许可按具体 checkpoint 再核 | 英语设备语音/指令识别可能更小；不适合本产品中文优先的默认项，先不进清单 |
| **本地对话（LLM）** | Qwen2.5-0.5B 级等 | ≥0.4–1.5 GB | 权重许可逐个核 | 体积/内存不合适，排最后、仅桌面实验 |

**结论：**「多模态」不必等于一个巨大的万能模型。文件管理器里拆成**文本嵌入（≈49 MB 默认包）+ 音频 ASR（≈57 MB）+ 图文 VLM（≈194 MB）**，可逐项选装、共享本地 ONNX Runtime。这样默认下载仍很小；识图问答可按需再加约 194 MB。若只想用「以图搜图/自然语言搜图」，SigLIP 双塔更省算力且检索更准，没必要用 VLM 生成长回答；若想「读懂图片、给一句描述或问它图里有什么」，选 SmolVLM。音频需求要分开看：Whisper=「说了什么」（语音转写），YAMNet=「听起来是什么」（环境/事件类别），二者加起来约 73 MB。

风险提醒：SmolVLM 官方 checkpoint 确实有 ONNX 导出，但它是三张 ONNX 图，C# 侧仍需 image processor、chat template、跨图缓存与生成循环；**下载清单已具备，运行时没有接线**。Whisper 的 ONNX 文件可下载，实际 ASR 还需要音频解码/重采样、log-Mel 特征、token 解码循环；也尚未接线。中文 VLM 质量、Android CPU 速度与实际峰值内存必须上机测，不能由模型参数数直接推断。

## 2. 运行时选型

| 运行时 | 许可 | AOT | 三平台 | 结论 |
|---|---|---|---|---|
| **ONNX Runtime**（`Microsoft.ML.OnnxRuntime`） | MIT | 使用 CPU 路径；本次接入后需由 AOT/Android CI 验证，真实模型首次运行仍需设备验收 | Windows/Linux x64、Android arm64 | **A2 已接入**，CPU 执行量化 BERT embedding |
| **Microsoft.ML.Tokenizers** | MIT | 纯托管，BERT WordPiece | 全平台 | **A2 已接入**，从模型随包 `vocab.txt` 构造分词器 |
| llama.cpp（`LLamaSharp`） | MIT | 需原生库，AOT 可用 | 三平台有 binding | 留给 A5（对话），且仅桌面 |
| sherpa-onnx | Apache-2.0 | 原生库，AOT 可用 | 三平台 | 备选 ASR/TTS；当前 ASR 清单选 ONNX Whisper tiny |
| 系统 TTS | 随系统 | — | Windows SAPI / Linux speech-dispatcher / Android TextToSpeech | A1，零依赖 |

## 3. 模型分发（v0.37 已落地）

模型几十 MB，**不进 git**，也不在安装包里；取模型是用户显式动作。

### 3.1 三方共用一份清单

`scripts/ai-models.manifest` 是唯一真源，纯文本、`|` 分隔——因为 bash 里没有 JSON 解析器，而脚本、PowerShell 与应用必须看到同一份定义。每个 HF 仓库 revision 固定为 40 位 commit SHA，不从浮动 `main` 拉取，避免下一次更新悄悄换模型。

```
model|<id>|<名称>|<用途>|<许可>|<许可链接>|<HF 仓库>|<固定 revision SHA>|<运行时>|<约体积 MB>|<默认下载 true/false>
file|<id>|<仓库内路径>
```

应用把它作为**嵌入资源**（`MdExplorer.Ai.Models.manifest`）编译进来，所以「脚本下的目录」与「程序找的目录」不会漂移；清单写错会在开发期就抛 `InvalidDataException`，而不是给用户一张残缺的表。

### 3.2 目录布局与校验

```
<模型目录>/<模型 id>/<仓库内路径>      例：…/models/text-embed-zh/onnx/model_quantized.onnx
<模型目录>/checksums.txt             首次下载后记录每个文件的 SHA-256
```

- 默认目录：`MDEXPLORER_MODELS` → 否则 `<应用数据目录>/models`（Linux `~/.local/share/MdExplorer/models`、Windows `%LOCALAPPDATA%\MdExplorer\models`）。应用与脚本用的是同一套优先级。
- 下载：先写入同目录 `.part`，`curl -C -` 断点续传、`-f` 让 4xx/5xx 直接失败；SHA-256 与大小验证通过后才原子改名，半截文件/错误页永远不会被应用看到；小于 1 KiB 的文件判为失败。`--force` 从头下；普通网络中断留下 `.part` 供续传。
- 校验：首次完整下载记录 SHA-256；之后每次运行按记录校验，坏文件重新下载；`--verify` 只校验不下载，缺 checksum 也会返回失败，不会把「没校验」报成通过。`--clean --only <id>` 只删除指定模型及其 checksum，不影响其它已安装模型。
- 镜像：设置 `HF_ENDPOINT=https://hf-mirror.com` 给下载脚本用；PowerShell 还可显式传 `-Mirror https://hf-mirror.com`。
- 默认调用只下文本嵌入包（≈49 MB）；可选 ASR/VLM **不自动下载**，需显式 `--only audio-asr-tiny` / `--only audio-classify-yamnet` / `--only image-vlm-smol`。`--all` 才下全部（约 316 MB）。PowerShell 对应 `-Only audio-asr-tiny` / `-Only audio-classify-yamnet` / `-Only image-vlm-smol` / `-All`。
- 命令：`scripts/fetch-ai-models.sh`（Linux/macOS/WSL，GNU 风格 `--list / --only / --all / --dir / --verify / --dry-run / --clean / --force`）、`scripts/fetch-ai-models.ps1`（Windows PowerShell 7，PowerShell 风格 `-List / -Only / -All / -Dir / -Verify / -DryRun / -Clean / -Force`）。

### 3.3 许可链

| 模型 | 权重许可 | 结论 |
|---|---|---|
| BAAI BGE small zh v1.5（`Xenova/bge-small-zh-v1.5` 的 ONNX 导出） | MIT | 可随 Apache-2.0 项目分发 |
| sentence-transformers all-MiniLM-L6-v2（`Xenova/all-MiniLM-L6-v2`） | Apache-2.0 | 同上 |
| Whisper tiny multilingual (`Xenova/whisper-tiny` ONNX 导出) | Apache-2.0（导出仓库模型卡；上游 OpenAI 源代码 MIT） | 按实际下载仓库的 Apache-2.0 元数据遵循，保留 upstream notices |
| YAMNet ONNX (`andrelgomes/yamnet-onnx`) | Apache-2.0（导出仓库） | 声音事件分类；模型权重 16 MB |
| SmolVLM-256M-Instruct ONNX | Apache-2.0（官方模型卡） | 多文件三图，包含视觉编码器与文本生成模型 |
| ONNX Runtime / Microsoft.ML.Tokenizers | MIT | Linux Native AOT 与 Android trim/AOT 发布通过 CI；Android APK 还会实检 ONNX ELF 依赖与 16 KiB 对齐 |

> 权重只在用户机器上下载，不进入本仓库；`models/` 已在 `.gitignore` 里。

## 4. 应用侧现状（v0.38：A2 第一片）

- `AiModelCatalog` 仍只做文件存在性/体积扫描；模型 Session 不在启动或设置页初始化。`OnnxTextEmbeddingService` 在用户显式建立 AI 索引/搜索时才按需加载 ONNX Session 与本地 `vocab.txt`。
- **模型路由**：中文查询优先 BGE small zh v1.5（512 维）；无中文字符时优先 MiniLM L6 v2（384 维）；某模型没安装则回退已安装的另一个。BERT 使用 `[CLS]`/`[SEP]`，动态序列长度最长 128，masked mean pooling + L2 normalize；BGE 查询加检索指令前缀。
- **搜索中心**新增「关键词 / AI 语义」模式和「建立 / 更新 AI 索引」。必须先配置并重建现有文件名索引、在设置中打开本地 AI、下载文本模型，再显式生成向量索引；索引文件变化、根集合变化或模型版本变化都会使 AI 索引过期并要求重建。
- 语义索引只覆盖用户加入的本地根，最多 20,000 项；保存路径/文件类型/大小/时间和每个可用文本模型的 int8 量化向量，不保存原始正文。文件名与父目录始终参与；仅对受支持的纯文本扩展名、≤2 MiB 文件最多读取前 4,000 字符作为摘要；不读 PDF、Office、压缩包、图片或 SAF。命中前再次核验文件时间/大小，stale 项不会被返回。`清除本地搜索索引` 同时清除文件名与 AI 向量快照。
- `AppSettings.EnableLocalAi` 默认关闭；关闭时保留本机 AI 向量文件但不运行推理/搜索。`MDEXPLORER_MODELS` 仍可覆盖模型目录。设置卡与搜索中心说明数据只在本机处理。
- **Android 原生库解析修正**：Microsoft.ML.OnnxRuntime 的桌面 Linux RID 原生库可能通过共享 `net10.0` 资产图混入 Android APK，并与 Android AAR 内的 Bionic 库发生重名冲突。Android app head 的窄作用域 MSBuild target 只移除 `linux-*` 的 native copy/publish 项；Linux/Windows 依赖不受影响。CI 会解包 arm64 APK，拒绝 glibc/Linux 依赖，并验证 ONNX ELF 的每个 `LOAD` 段均按 16 KiB 对齐。
- **验收边界**：CI 使用确定性的假 embedding 覆盖重建、排序、持久化、模型版本失效、清除与开关；Linux Native AOT、Android trimming/AOT 发布及 APK 原生库检查均通过。尚未下载真实权重，BGE/MiniLM 实际 ONNX 推理、中文/英文搜索质量、建索引耗时和设备内存/延迟仍待实机验收。

## 5. 后续计划

1. **A2 后续打磨**：在真实模型下对比 token IDs/embedding 与 Transformers.js 或 sentence-transformers，校准 BERT basic tokenizer、BGE query 前缀与阈值；为大索引增加增量更新与可调上限，并测试低内存 Android。
2. **A3 图像搜索/标签**：先做图文双塔 SigLIP（比 VLM 生成文本轻且检索目标更匹配），图片/查询嵌入共享向量库；再评估 YAMNet 将 521 类声音标签与音频元数据入索引。分开返回「语义相似」和「分类标签」，不写文件、不改 EXIF。
3. **A4 音频转写**：接 Whisper tiny q4f16：平台解码音频为单声道 16 kHz → log-Mel → greedy decoder；文件长度/时长设上限、取消/进度/导出 SRT；不支持的媒体容器就走「系统应用打开」。
4. **A5 图像问答（可选）**：SmolVLM-256M q4f16 需要视觉 encoder / embed_tokens / decoder 三个 ONNX session、处理器 resize/patch 切图与生成循环；先测峰值内存、速度及中文效果，再决定 Android 是否开放。
5. **验收**：真实模型下测 100/1,000 文件索引耗时、中文/英文查询 top-k 质量、冷/热查询延迟与 Android 峰值内存；本地模型缺失/损坏或 ONNX Session 初始化失败都必须明确报错且不退化成假语义结果。

## 6. 隐私与安全边界

- **无网络**：应用运行期不发起任何 HTTP 请求；模型由用户手动下载。没有「首次启动偷偷下模型」。
- **无遥测**：不上报查询、路径或统计。
- **数据不出本机**：索引与向量都在应用数据目录；用户可整目录删除。
- **模型文件可信边界**：ONNX 模型是数据而非代码，但仍按「用户自己下载的外部文件」对待——只从清单里的路径加载，路径不允许逃出模型目录。

## 7. 风险与未验证项

- **没有实际设备推理证据**：CI 已验证 Linux Native AOT、Android trimming/AOT 发布，以及 APK 内是 Bionic ONNX 库且 16 KiB 页面对齐；仍未在真机上加载模型、执行推理或测性能。
- **模型仓库路径可能漂移**：清单里的 HF 路径写的是 `Xenova/*` 镜像仓库；脚本失败时会打印完整 URL 与镜像提示，改清单即可，不需要重编译（应用读的是编译进去的同一份文件，改完重编一次）。
- **没有真机推理证据**：本仓还没有在设备上使用下载权重执行真实推理，因此没有搜索质量或性能数字；验收需补「100/1000 个文件索引耗时、单次查询延迟与 Android 峰值内存」。
- **int8 量化的精度损失**未评估：先按官方量化版发布，检索质量不达标再换 fp32（体积 ×4）。

## 8. 参考

- BGE small zh v1.5 模型卡与许可：<https://huggingface.co/BAAI/bge-small-zh-v1.5>
- all-MiniLM-L6-v2 模型卡：<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>
- SmolVLM-256M 模型卡、ONNX 三图示例、Apache-2.0 与文件体积：<https://huggingface.co/HuggingFaceTB/SmolVLM-256M-Instruct>
- Whisper tiny multilingual 模型卡/39M 参数与多语言说明：<https://huggingface.co/openai/whisper-tiny>
- Whisper ONNX 导出仓库模型卡（license: Apache-2.0）：<https://huggingface.co/Xenova/whisper-tiny>
- Whisper 上游 OpenAI LICENSE（MIT）：<https://github.com/openai/whisper/blob/main/LICENSE>
- YAMNet ONNX (AudioSet 521 类，16 MB) 模型卡/Apache-2.0：<https://huggingface.co/andrelgomes/yamnet-onnx>
- Hugging Face Hub model tree API（ONNX q4f16 文件大小快照）：<https://huggingface.co/api/models/HuggingFaceTB/SmolVLM-256M-Instruct/tree/main/onnx?expand=false> / <https://huggingface.co/api/models/Xenova/whisper-tiny/tree/main?recursive=true&expand=false>
- ONNX Runtime 许可与 Native AOT 说明：<https://github.com/microsoft/onnxruntime>
- .NET Android 中 ONNX Linux RID 与 Android AAR 冲突及 workaround：<https://github.com/microsoft/onnxruntime/issues/29270>
- Android 16 KiB 页面对齐要求：<https://developer.android.com/guide/practices/page-sizes>
- Microsoft.ML.Tokenizers：<https://github.com/dotnet/machinelearning>
