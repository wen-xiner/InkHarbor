# InkHarbor（墨坞）

> 本地模型的私人写作工坊 —— 人出情节大纲，模型照选定范文的文风把稿子写出来，人工改完封存。
> **全程离线，文章不出设备。**

一个跑在自己电脑上的中文写作工具。它不联网、不接任何云端 API，只跟本机的
Ollama 说话。所有素材（范文、底稿、终稿）都落在本地目录里，模型文件也只读。

**当前进度：M1 已完成（2026-10-16）**，核心闭环已端到端跑通并通过真机验收。

---

## 它解决什么问题

用大模型续写小说，最烦的两个点：

1. **文风不对**——模型自由发挥，写出来是"AI 味"的通用腔调，不是你要的那种文风。
2. **稿子不见了**——让模型改一遍，原来的版本被覆盖掉，改坏了回不去。

InkHarbor 的做法：

- **范文当"文风样本"**：你把喜欢的段落放进范文库，生成时选几篇当配方（few-shot），
  模型照这个腔调写。范文库就是你的文风字典。
- **底稿不可变**：模型生成的 v1 落盘即只读，之后每一次修改都落成**新版本**
  （v2、v3……），旧版本永远在。确认满意了才手动封进终稿，
  终稿同名也只会加序号，不会覆盖。

---

## 三条硬规矩

这几条是写进代码里的，不是口头约定：

| 规矩 | 实现位置 |
|---|---|
| **底稿不可变** | `DraftService.WriteVersionFile()` 发现目标版本文件已存在**直接抛异常**，这是最后一道闸 |
| **只连本机模型** | `OllamaClient` 里不出现任何云端地址或 Key 字段，只认 `localhost:11434` |
| **终稿不覆盖** | `SaveAsFinal()` 同名自动加 ` (2)` 序号 |

---

## 界面

一屏四区：顶栏（模型状态 / 工作区路径）· 左栏双 Tab（**范文库** / **作品**）·
组稿区（配方胶囊 + 情节大纲 + 生成按钮）· 成品区（可编辑正文 + 存底稿 / 入终稿）。

界面施工图见 [`InkHarbor-界面设计图.html`](InkHarbor-界面设计图.html)，直接用浏览器打开。

---

## 技术栈

| 项 | 值 |
|---|---|
| 语言 / 框架 | C# + **WPF（.NET 9）** |
| 目标框架 | `net9.0`（Core 类库）/ `net9.0-windows`（WPF 应用） |
| 推理后端 | 本机 **Ollama**（`http://localhost:11434`） |
| 主力模型 | `qwen3:4b-instruct-2507-q4_K_M`（2.5 GB，Q4_K_M，**非思考版**） |
| 持久化 | `System.Text.Json` + 临时文件原子写 |
| 依赖 | **零第三方 NuGet 依赖**，只用 BCL |

选 WPF 的理由很朴素：本机已装 .NET 9 SDK，**零新增工具链**。
（Electron 要装 npm 一堆、PySide6 要装 Qt、Tauri 要装 rustc——都因为要新装东西被排除。）

---

## 目录结构

```
InkHarbor.sln
├─ src/
│  ├─ InkHarbor.Core/          类库，无 UI 依赖，可单测
│  │  ├─ Models/               DTO 与枚举
│  │  ├─ Services/             工作区 / 范文 / 预设 / 底稿 / 提示词 / Ollama 客户端
│  │  └─ Json/JsonStore.cs     统一 JSON 读写（原子写 + 中文不转义）
│  └─ InkHarbor.App/           WPF 界面
│     ├─ ViewModels/           MVVM
│     └─ Views/                弹窗（取名 / 范文预览）
└─ （运行时数据目录，默认 D:\Test\InkHarbor）
   ├─ corpus/                  范文库，文件名即标签：题材_人称_节奏_编号.md
   ├─ presets/                 配方预设，一篇一个 .json
   ├─ drafts/<标题>/           v1.md、v2.md…… + meta.json
   └─ final/                   终稿封存
```

工作区根目录可用环境变量 `INKHARBOR_ROOT` 覆盖，默认 `D:\Test\InkHarbor`。

---

## 快速开始

**1. 装 Ollama 并拉模型**

```bash
# 从 https://ollama.com/download 装 Windows 版
ollama pull qwen3:4b-instruct-2507-q4_K_M
```

> 想省 C 盘空间，先建环境变量 `OLLAMA_MODELS` 指到别的盘。

**2. 编译运行**

```bash
dotnet build InkHarbor.sln
dotnet run --project src/InkHarbor.App
```

或者直接跑产物 `src/InkHarbor.App/bin/Debug/net9.0-windows/InkHarbor.exe`。

**3. 放两篇范文**

往 `corpus/` 里丢几个 `.md` 文件，文件名按 `题材_人称_节奏_编号.md` 起
（比如 `都市_第一人称_冷幽默_01.md`），标签就自动解析出来了。

**4. 走一遍六步流程**

挑范文 → 配料方 → 写情节大纲 → 生成（流式）→ 手动改 → 存底稿 / 入终稿。

---

## 关于模型选型：为什么不用原版 `qwen3:4b`

这是个踩过的坑，记下来免得重蹈覆辙。

`qwen3:4b` 是**思考版**，它的 chat template 末尾无条件给 assistant 回合塞了一个
`<think>`，所以模型必然先想一大段再动笔。请求侧试过四种"关思考"的办法，全部无效或有害：

| 请求方式 | 结果 |
|---|---|
| 带 `think:false` | Ollama 不再分离思考，**思考原文直接混进正文**，污染输出 |
| 不带 `think` 字段 | 思考分离到 `message.thinking`，正文干净，但思考 token 照样吃输出预算 |
| 消息尾加 `/no_think` | **无效**，模型照想 |
| assistant 预填充空闭合思考块 | **无效**，拦不住，模型习惯性思考 |

实测代价（本机 Intel Arc 130V 核显）：

| 模型 | 思考块 | 正文 | 耗时 | done_reason |
|---|---|---|---|---|
| `qwen3:4b` | 3570 字（吃光输出预算） | **0 字** | 322 s | `length`（截断） |
| `qwen3:4b-instruct-2507-q4_K_M` | 0 字 | **699 字** | 74 s | `stop` |

所以主力换成**非思考版**：不去想，速度和正文纯净度一起解决，
而且它本来就是方案里定的微调底模，后面上 LoRA 不会串种。

代码里 `MainViewModel.PreferredModels` 决定默认选哪个模型，`qwen3:4b` 仍留在下拉里可选；
另一层保险是 `NumPredict = clamp(目标字数 × 2 + 2048, 2048, 8192)`，
`done_reason == length` 时状态栏会明确报警"被截断"。

---

## 文档

| 文件 | 作用 |
|---|---|
| [`InkHarbor-总体方案.md`](InkHarbor-总体方案.md) | 总纲：模型策略 / 数据规则 / 分期 / 技术栈选型 |
| [`InkHarbor-使用说明书.md`](InkHarbor-使用说明书.md) | 装好之后怎么用（面向使用者） |
| [`本地模型写作工作流搭建教程.md`](本地模型写作工作流搭建教程.md) | 从零装 Ollama 的步骤细节 |
| [`InkHarbor-界面设计图.html`](InkHarbor-界面设计图.html) | 界面施工图 |

---

## 开发路线

- **M1 · 核心闭环** —— 已完成。挑范文 → 配方（token 统计）→ 大纲生成（流式）→
  自动存底稿 → 手动编辑 → 确认入终稿。
- **M2 · AI 修订** —— 计划中。模型自我检查并按建议改稿，采纳制（不自动覆盖）；
  版本历史与回退。数据结构已预留：`DraftMeta.Versions` 是数组、
  `VersionSource.Ai` 常量、`PromptBuilder.BuildSelfCheckPrompt()` 已写好未接。
- **M3 · 范文自动推荐** —— 计划中。按大纲自动从范文库里挑配方，
  `DraftMeta.CorpusFiles` 已预留留痕字段。
- **M4+ · 多文风 LoRA** —— 每种文风一个 LoRA，各自从干净底模训，`ollama create` 成独立标签。

界面里 M2/M3 的按钮（AI 修订 / 版本历史 / 按大纲自动推荐）已经画好但**置灰**，没有写逻辑。
