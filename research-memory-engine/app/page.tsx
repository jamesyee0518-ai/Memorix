"use client";

import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import {
  Archive,
  ArrowRight,
  Bot,
  BrainCircuit,
  Check,
  ChevronDown,
  Cloud,
  Database,
  FileText,
  Fingerprint,
  GitBranch,
  Globe2,
  Inbox,
  Layers3,
  Languages,
  Library,
  Lock,
  LucideIcon,
  MessageSquareText,
  Network,
  NotebookTabs,
  Search,
  ScanSearch,
  Server,
  ShieldCheck,
  Moon,
  Sparkles,
  Smartphone,
  Sun,
  TerminalSquare,
  UploadCloud,
  Workflow,
  Zap
} from "lucide-react";
import { cn } from "@/lib/utils";
import { DownloadButton } from "@/components/download-button";

type Lang = "zh" | "en";
type Theme = "dark" | "light";

const copy = {
  zh: {
    nav: ["产品", "功能", "多语言资料库", "使用场景", "本地优先", "Agent 接入", "下载", "FAQ"],
    join: "下载客户端",
    demo: "查看演示",
    productName: "Memorix",
    productEn: "你的私人 AI 记忆体",
    kicker: "你的私人 AI 记忆体",
    heroTitle: "让碎片信息，变成你的 AI 知识资产",
    heroSubtitle:
      "Memorix 是一款本地优先、可选云端、支持手机采集和 Agent 调用的 AI 知识资产工具。它可以把网页、PDF、图片、录音、笔记和聊天输入，自动清洗、摘要、结构化、检索、问答并生成报告。",
    ctaPrimary: "下载客户端",
    ctaSecondary: "了解工作流",
    workflow: ["采集", "语言识别", "结构化", "中文索引", "检索/问答", "报告", "Agent"],
    painTitle: "你保存了很多信息，但真正能复用的很少",
    solutionTitle: "从“收藏信息”升级为“知识资产”",
    solutionText:
      "Memorix 不只是帮你保存资料，而是把原始资料加工成可搜索、可理解、可追溯、可输出、可被 Agent 调用的长期资产。",
    featuresTitle: "为研究工作流设计的知识资产系统",
    modesTitle: "你可以选择数据在哪里工作",
    howTitle: "从采集到报告，再到 Agent 调用",
    useTitle: "适合持续积累和复用资料的工作",
    privacyTitle: "本地优先，不等于拒绝云端",
    privacyText:
      "Memorix 默认让重要数据留在本地。你可以选择是否启用云端 Inbox、云同步或云端模型。系统会清楚展示数据保存在哪里、模型在哪里运行、哪些内容会发送到云端。",
    agentTitle: "让你的知识库成为 Agent 的长期记忆",
    agentText:
      "通过本地 MCP Server，Claude、Cursor、Hermes、本地自动化脚本和其他 Agent 可以在授权范围内搜索、问答、读取文档和获取报告。",
    compareTitle: "不是保存信息，而是加工信息、理解信息、复用信息",
    ctaTitle: "下载 Memorix，开始构建你的 AI 知识资产",
    bookDemo: "预约演示",
    roadmap: "查看产品路线"
  },
  en: {
    nav: ["Product", "Features", "Multilingual", "Use Cases", "Local-first", "Agent Integration", "Download", "FAQ"],
    join: "Download App",
    demo: "View Demo",
    productName: "Memorix",
    productEn: "Your Private AI Memory",
    kicker: "Your Private AI Memory",
    heroTitle: "Turn Scattered Information into Your Private AI Memory",
    heroSubtitle:
      "Memorix is a local-first AI knowledge asset engine with optional cloud sync, mobile capture, and agent integration. It turns web pages, PDFs, images, audio notes, text snippets, and chat inputs into structured, searchable, reusable knowledge.",
    ctaPrimary: "Download App",
    ctaSecondary: "See Workflow",
    workflow: ["Capture", "Detect", "Structure", "Chinese Index", "Search/Ask", "Report", "Agent"],
    painTitle: "You save a lot of information. Very little becomes reusable knowledge.",
    solutionTitle: "From Saving Information to Building Knowledge Assets",
    solutionText:
      "Memorix does not just store information. It transforms raw materials into searchable, structured, traceable, report-ready, and agent-accessible knowledge assets.",
    featuresTitle: "A knowledge asset system designed for research workflows",
    modesTitle: "Choose Where Your Knowledge Lives",
    howTitle: "From capture to reports, then agent access",
    useTitle: "Built for work that compounds over time",
    privacyTitle: "Local-first does not mean cloudless",
    privacyText:
      "Memorix keeps important data local by default. You decide whether to enable Cloud Inbox, cloud sync, or cloud models. The app clearly shows where your data lives, where models run, and what may be sent to the cloud.",
    agentTitle: "Turn Your Knowledge Base into Long-Term Memory for Agents",
    agentText:
      "Through a local MCP Server, Claude, Cursor, Hermes, local automation scripts, and other agents can search, ask, read documents, and retrieve reports within your authorized scope.",
    compareTitle: "Not just saving information, but processing, understanding, and reusing it.",
    ctaTitle: "Download Memorix and Start Building Your AI Knowledge Assets",
    bookDemo: "Book a Demo",
    roadmap: "View Roadmap"
  }
};

const pain = [
  ["信息分散", "Scattered information", "资料散落在收藏夹、聊天记录、网盘、笔记、PDF 和截图里。", "Knowledge is scattered across bookmarks, chats, drives, notes, PDFs, and screenshots.", Network],
  ["找不回来", "Hard to find again", "曾经看过的重要内容，真正需要时很难快速定位。", "Important information you once read becomes hard to find when you need it.", Search],
  ["缺少结构", "Missing structure", "收藏只是保存，无法自动提取摘要、标签、实体、风险和机会。", "Saving is not understanding. Most tools do not extract summaries, tags, entities, risks, or opportunities.", Layers3],
  ["难以输出", "Hard to produce output", "资料很多，但生成日报、周报、研究报告和文章素材依然需要大量人工整理。", "Turning raw materials into briefs, reports, and writing assets still takes too much manual work.", FileText],
  ["Agent 无法调用", "Agents cannot access it", "个人知识库无法自然接入 Claude、Cursor、Hermes 或本地 Agent 工作流。", "Personal knowledge is often inaccessible to Claude, Cursor, Hermes, or local agents.", Bot],
  ["语言成为复用门槛", "Language becomes a barrier", "外文资料常因检索词、阅读成本和术语差异而沉睡在资料库中。", "Foreign-language sources remain unused because of search vocabulary, reading effort, and inconsistent terminology.", Languages]
] as const;

const valueLayers = [
  ["记忆层", "Memory Layer", "保存网页、PDF、文本、图片、录音和文件，保留原始来源。", "Save web pages, PDFs, text, images, audio, and files with original sources preserved.", Archive],
  ["理解层", "Understanding Layer", "识别文档与分块语言，为外文生成中文标题、摘要、关键词、实体和按需译文，同时保留原文。", "Detect document and chunk languages, create Chinese titles, summaries, keywords, entities, and on-demand translations while preserving originals.", BrainCircuit],
  ["复用层", "Reuse Layer", "用中文统一检索多语言资料，以中文问答和生成报告，并保留可回溯的原文引用。", "Search multilingual sources in Chinese, ask and report in Chinese, and trace citations to original evidence.", GitBranch]
] as const;

const features = [
  ["多源资料导入", "Multi-source import", "支持 URL、PDF、Markdown、文本、图片、录音和手机聊天式输入。", "Import URLs, PDFs, Markdown, text, images, audio, and mobile chat-style captures.", UploadCloud],
  ["Inbox 收件箱", "Inbox", "所有碎片信息先进入 Inbox，由 AI 推荐专题和处理方式，避免污染正式知识库。", "All inputs first enter Inbox, where AI suggests topics and processing actions before they enter your knowledge base.", Inbox],
  ["AI 清洗与结构化", "AI cleaning and structuring", "自动清洗网页和文档，生成摘要、结论、关键要点、标签、实体、风险、机会和价值评分。", "Clean and structure content into summaries, conclusions, key points, tags, entities, risks, opportunities, and value scores.", Sparkles],
  ["多语言自适应处理", "Adaptive multilingual processing", "自动识别中文、外文和混合内容，按资料价值生成中文标题、摘要、关键词、译文和索引。", "Detect Chinese, foreign-language, and mixed content, then build Chinese titles, summaries, keywords, translations, and indexes as needed.", Languages],
  ["专题知识库", "Topic knowledge bases", "按 AI 行业、竞品研究、项目资料、投研资料、技术文档等专题组织知识。", "Organize knowledge by AI research, competitor analysis, project knowledge, investment research, and technical documentation.", Library],
  ["混合检索", "Hybrid retrieval", "结合中文全文、关键词、向量、跨语言语义、术语、实体、时间与价值评分，让中文问题召回中文和外文资料。", "Combine Chinese full-text, keywords, vectors, cross-language semantics, terminology, entities, time, and value ranking.", Search],
  ["RAG 问答", "RAG Q&A", "基于多语言资料进行中文问答，回答以中文呈现，并引用可回溯的外文原始证据。", "Ask in Chinese across multilingual sources, read Chinese-form answers, and trace citations to original-language evidence.", MessageSquareText],
  ["自动报告", "Automatic reports", "一键生成日报、周报、专题研究报告、竞品分析和写作素材。", "Generate daily briefs, weekly reports, topic research reports, competitor analysis, and writing materials.", FileText],
  ["Markdown / Obsidian 导出", "Markdown / Obsidian export", "支持导出为 Markdown 和 Obsidian Vault，数据开放、可迁移、不锁定。", "Export to Markdown and Obsidian Vault. Your data stays open, portable, and under your control.", NotebookTabs],
  ["本地模型与云模型", "Local and cloud models", "支持 Ollama、LM Studio、本地模型和用户自带 OpenAI-compatible API。", "Support Ollama, LM Studio, local models, and user-provided OpenAI-compatible APIs.", Server],
  ["MCP / Agent 接入", "MCP / Agent access", "通过本地 MCP Server，让 Claude、Cursor、Hermes 和本地 Agent 调用你的知识库。", "Expose your knowledge base to Claude, Cursor, Hermes, and local agents through a local MCP Server.", TerminalSquare]
] as const;

const modes = [
  ["本地工作区", "Local Workspace", "数据保存在本机，使用 SQLite 和本地 Vault，支持本地模型和本地 MCP，适合隐私资料、研究资料和开发者工作流。", "Data stays on your machine with SQLite and a local Vault. Supports local models and local MCP. Ideal for private research and developer workflows.", Database],
  ["云端工作区", "Cloud Workspace", "数据保存在云端，适合多设备访问、轻量用户、团队协作和云端处理。", "Data is stored in the cloud for multi-device access, lightweight users, team collaboration, and cloud processing.", Cloud],
  ["混合工作区", "Hybrid Workspace", "本地保存主知识库，手机端采集进入云端 Inbox，再同步到桌面端处理，兼顾隐私和便利。", "Keep the main knowledge base local, capture on mobile through Cloud Inbox, and sync into the desktop app for processing.", Smartphone]
] as const;

const steps = [
  ["创建工作区", "Create a workspace", "选择本地、云端或混合模式。", "Choose local, cloud, or hybrid mode."],
  ["创建专题", "Create topics", "例如 AI 行业研究、竞品分析、项目知识库、技术资料库。", "For AI research, competitor analysis, project knowledge, or technical documentation."],
  ["导入资料", "Import sources", "粘贴网页链接，上传 PDF，输入笔记，或用手机发送资料到 Inbox。", "Paste URLs, upload PDFs, write notes, or send materials from mobile to Inbox."],
  ["AI 自适应处理", "Adaptive AI processing", "系统解析、清洗并识别文档和分块语言，生成中文理解层、标签、实体、分块与多路索引。", "Parse, clean, detect document and chunk languages, and build a Chinese understanding layer, entities, chunks, and multiple indexes."],
  ["中文搜索和问答", "Search and ask in Chinese", "直接用中文检索中文与外文资料；回答显示中文结果，并可展开查看原文证据。", "Search Chinese and foreign sources in Chinese, read answers in Chinese, and inspect original evidence."],
  ["生成报告", "Generate reports", "输出日报、周报、专题报告、选题库或文章大纲。", "Create daily briefs, weekly reports, topic reports, idea lists, or article outlines."],
  ["接入 Agent", "Connect agents", "通过 MCP / API 让 Claude、Cursor、Hermes 或自动化脚本调用知识库。", "Let Claude, Cursor, Hermes, or automation scripts access your knowledge through MCP / API."]
] as const;

const useCases = [
  ["AI 行业日报", "AI industry briefs", "自动汇总 AI 新闻、技术博客、公司动态和论文摘要，生成每日简报。", "Summarize AI news, technical blogs, company updates, and papers into daily briefs.", Globe2],
  ["竞品研究库", "Competitor research", "持续跟踪竞品官网、更新日志、用户评价和新闻报道。", "Track competitor websites, changelogs, user reviews, and news coverage.", Fingerprint],
  ["项目知识库", "Project knowledge", "沉淀需求文档、技术方案、会议纪要、合同资料和开发规范。", "Organize requirements, technical plans, meeting notes, contracts, and development specs.", Library],
  ["创作者素材库", "Creator source library", "把资料转成选题、脚本、大纲、观点和素材片段。", "Turn sources into topics, scripts, outlines, opinions, and reusable snippets.", Sparkles],
  ["Agent 长期记忆", "Agent long-term memory", "让本地 Agent 调用你的私有知识，而不是每次重新上传上下文。", "Give local agents persistent access to your private knowledge without re-uploading context every time.", Bot],
  ["全球产业与技术研究", "Global industry and technology research", "持续导入英文、日文、韩文等海外资料，用中文统一搜索、问答和生成报告。", "Continuously ingest international sources, search and ask across them in Chinese, and generate reports with original evidence.", Globe2]
] as const;

const trust = [
  ["原始资料默认本地保存", "Original sources stay local by default", Lock],
  ["支持 Markdown / Obsidian 开放格式", "Open Markdown / Obsidian-compatible formats", NotebookTabs],
  ["本地模型优先，可选云端模型", "Local models first, cloud models optional", Server],
  ["云端能力必须由用户主动开启", "Cloud features are opt-in", ShieldCheck],
  ["多语言处理可按工作区选择本地或云端模型", "Choose local or cloud models for multilingual processing", Languages]
] as const;

const faq = [
  ["外文资料会被全部自动翻译吗？", "Will every foreign-language source be fully translated?", "不会。默认先生成中文标题、摘要、关键词和实体；命中的段落可按需翻译，核心资料可选择完整中文化。原文始终保留。", "No. By default Memorix creates Chinese titles, summaries, keywords, and entities. Relevant chunks can be translated on demand, while core sources can be fully localized. Originals are always preserved."],
  ["可以只用中文搜索英文资料吗？", "Can I search English sources only in Chinese?", "可以。系统结合中文全文与语义索引、术语扩展和外文跨语言向量，使中文查询能够召回中文和外文资料。", "Yes. Chinese full-text and semantic indexes, terminology expansion, and cross-language vectors let Chinese queries retrieve Chinese and foreign-language sources."],
  ["中文回答能查看外文原文吗？", "Can I inspect the original behind a Chinese answer?", "可以。引用会区分中文原文、系统译文、AI 摘要和外文原文，并在可用时返回原始文档、章节或页码。", "Yes. Citations distinguish original Chinese, machine translations, AI summaries, and foreign-language originals, linking back to the document, section, or page when available."],
  ["多语言处理一定会把资料发送到云端吗？", "Does multilingual processing always send data to the cloud?", "不一定。本地、云端或混合处理由工作区配置决定；启用云端能力前会明确提示可能发送的数据范围。", "Not necessarily. Local, cloud, or hybrid processing is controlled by workspace settings, with disclosure before cloud processing is enabled."],
  ["支持哪些语言？", "Which languages are supported?", "首期重点保障简体中文和英文；繁体中文、日文、韩文及混合语言资料将按实际版本与评测状态标注。", "The initial release prioritizes Simplified Chinese and English. Traditional Chinese, Japanese, Korean, and mixed-language sources are labeled according to release and evaluation status."],
  ["支持哪些桌面系统？", "Which desktop systems are supported?", "计划提供 Apple Silicon Mac 的 DMG 和 64 位 Windows 安装程序。网站只在正式下载地址可用时开放对应按钮。", "Memorix plans to provide a DMG for Apple Silicon Macs and a Windows x64 installer. Each button is enabled only when its official download URL is available."],
  ["这个产品和 Obsidian 有什么区别？", "How is this different from Obsidian?", "Obsidian 是优秀的本地笔记和链接工具。Memorix 更偏向资料采集、AI 清洗、结构化、检索、报告生成和 Agent 调用，可以导出到 Obsidian，而不是替代它。", "Obsidian is excellent for local notes and linked thinking. Memorix focuses on capture, AI processing, structured retrieval, reports, and agent access. It can export to Obsidian rather than replace it."],
  ["数据一定要上传云端吗？", "Does my data have to be uploaded?", "不需要。默认可以本地工作。云端 Inbox、云同步和云模型都是可选能力。", "No. You can work locally by default. Cloud Inbox, cloud sync, and cloud models are optional."],
  ["是否支持本地模型？", "Does it support local models?", "支持 Ollama、LM Studio、本地模型，以及 OpenAI-compatible API。", "Yes. It supports Ollama, LM Studio, local models, and OpenAI-compatible APIs."],
  ["手机端是完整 App 吗？", "Is the mobile app full-featured?", "MVP 阶段手机端更偏采集入口，用聊天式输入、分享和上传进入 Inbox，复杂整理主要在桌面端完成。", "In the MVP, mobile is mainly for capture through chat-style input, sharing, and uploads into Inbox. Deeper curation happens on desktop."],
  ["为什么需要 Inbox？", "Why does it need an Inbox?", "Inbox 用来暂存碎片资料，让 AI 先推荐专题、标签和处理方式，避免把低价值资料直接污染正式知识库。", "Inbox buffers raw inputs so AI can suggest topics, tags, and actions before low-value material pollutes the formal knowledge base."],
  ["是否支持团队版？", "Will there be a team edition?", "规划支持小团队协作、共享专题和权限管理，但本地优先个人工作区会先完成。", "Team collaboration, shared topics, and permissions are planned, after the local-first personal workflow is solid."],
  ["是否支持导出？", "Can I export my data?", "支持 Markdown、Obsidian Vault 和报告导出，目标是不锁定数据。", "Yes. Markdown, Obsidian Vault, and report exports are supported. The goal is to avoid lock-in."],
  ["Agent 如何调用知识库？", "How do agents access the knowledge base?", "通过本地 MCP Server 或 API，Agent 可以在授权范围内搜索专题、问答、读取文档和获取报告。", "Through a local MCP Server or API, agents can search topics, ask questions, read documents, and retrieve reports within authorized scopes."],
  ["MVP 阶段有哪些功能？", "What is included in the MVP?", "优先包含资料导入、Inbox、AI 摘要与标签、专题库、混合检索、RAG 问答、报告生成和 Markdown 导出。", "The MVP prioritizes import, Inbox, AI summaries and tags, topic bases, hybrid search, RAG Q&A, report generation, and Markdown export."],
  ["适合哪些用户？", "Who is it for?", "适合 AI 研究者、产业研究者、产品经理、创业者、技术自媒体、开发者和咨询顾问。", "It is for AI researchers, industry analysts, product managers, founders, technical creators, developers, and consultants."]
] as const;

const multilingualStages = [
  ["自动识别", "Detect", "识别文档和分块的主语言、混合比例与内容类型。", "Identify primary language, language mix, and content type at document and chunk level.", ScanSearch],
  ["自适应处理", "Adapt", "中文直接规范化；外文按策略生成中文理解层，代码、公式和引用保持原样。", "Normalize Chinese, localize foreign content by policy, and preserve code, formulas, and references.", GitBranch],
  ["多路索引", "Index", "建立中文全文、中文语义、术语和外文跨语言检索路径。", "Build Chinese full-text, semantic, terminology, and original cross-language retrieval paths.", Layers3],
  ["中文使用，原文举证", "Use in Chinese, cite originals", "用中文搜索、问答和生成报告，每条关键结论都可返回原文。", "Search, ask, and report in Chinese while tracing key claims to original evidence.", Languages],
] as const;

const localizationLevels = [
  ["L1", "快速理解", "Quick understanding", "生成中文标题、摘要、关键词和实体，适合大规模资料入库。", "Generate Chinese titles, summaries, keywords, and entities for high-volume ingestion.", "默认", "Default"],
  ["L2", "按需阅读", "On-demand reading", "资料被检索或问答命中时，翻译相关分块并缓存复用。", "Translate relevant chunks when retrieved, then cache them for reuse.", "智能", "Adaptive"],
  ["L3", "深度中文化", "Deep localization", "为核心专题或高价值资料建立完整中文内容层。", "Build a complete Chinese content layer for core topics or high-value sources.", "深度", "Deep"],
] as const;

function pick(lang: Lang, zh: string, en: string) {
  return lang === "zh" ? zh : en;
}

function Button({ children, variant = "primary", className }: { children: React.ReactNode; variant?: "primary" | "secondary" | "ghost"; className?: string }) {
  return (
    <button
      className={cn(
        "inline-flex h-11 items-center justify-center gap-2 rounded-md px-5 text-sm font-medium transition",
        variant === "primary" && "bg-cyan-300 text-slate-950 hover:bg-cyan-200 shadow-glow",
        variant === "secondary" && "border border-white/12 bg-white/8 text-white hover:bg-white/12",
        variant === "ghost" && "text-slate-300 hover:text-white",
        className
      )}
    >
      {children}
    </button>
  );
}

function Section({ id, children, className }: { id?: string; children: React.ReactNode; className?: string }) {
  return (
    <section id={id} className={cn("mx-auto w-full max-w-7xl px-5 py-20 sm:px-8", className)}>
      {children}
    </section>
  );
}

function SectionTitle({ eyebrow, title, text }: { eyebrow?: string; title: string; text?: string }) {
  return (
    <div className="mx-auto mb-10 max-w-3xl text-center">
      {eyebrow && <p className="mb-3 text-sm font-medium text-cyan-300">{eyebrow}</p>}
      <h2 className="text-balance text-3xl font-semibold tracking-normal text-white md:text-5xl">{title}</h2>
      {text && <p className="mt-5 text-base leading-8 text-slate-300 md:text-lg">{text}</p>}
    </div>
  );
}

function IconCard({ icon: Icon, title, text }: { icon: LucideIcon; title: string; text: string }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      whileInView={{ opacity: 1, y: 0 }}
      whileHover={{ y: -6, scale: 1.015 }}
      transition={{ type: "spring", stiffness: 280, damping: 24 }}
      viewport={{ once: true, margin: "-80px" }}
      className="glass interactive-card group rounded-lg p-6"
    >
      <div className="mb-5 flex h-11 w-11 items-center justify-center rounded-md bg-cyan-300/12 text-cyan-200 transition group-hover:bg-cyan-300/20 group-hover:text-cyan-100">
        <Icon size={22} />
      </div>
      <h3 className="text-lg font-semibold text-white">{title}</h3>
      <p className="mt-3 text-sm leading-7 text-slate-300">{text}</p>
    </motion.div>
  );
}

function HeroVisual({ lang }: { lang: Lang }) {
  const t = copy[lang];
  const chips = lang === "zh" ? ["网页", "PDF", "图片", "录音", "笔记", "聊天"] : ["Web", "PDF", "Images", "Audio", "Notes", "Chats"];
  return (
    <div className="relative min-h-[460px] overflow-hidden rounded-lg border border-white/10 bg-slate-950/80 p-5 shadow-2xl">
      <div className="absolute inset-0 grid-mask opacity-60" />
      <div className="relative flex items-center justify-between border-b border-white/10 pb-4">
        <div>
          <p className="text-sm font-medium text-white">{t.productName}</p>
          <p className="text-xs text-slate-400">{t.kicker}</p>
        </div>
        <div className="flex items-center gap-2 rounded-md border border-emerald-300/20 bg-emerald-300/10 px-3 py-1 text-xs text-emerald-200">
          <span className="h-2 w-2 rounded-full bg-emerald-300" />
          Local
        </div>
      </div>
      <div className="relative mt-6 grid gap-4 lg:grid-cols-[0.8fr_1.2fr]">
        <div className="space-y-3">
          {chips.map((item, index) => (
            <motion.div
              key={item}
              initial={{ opacity: 0, x: -16 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: index * 0.08 }}
              className="flex items-center justify-between rounded-md border border-white/10 bg-white/6 px-4 py-3"
            >
              <span className="text-sm text-slate-200">{item}</span>
              <ArrowRight size={15} className="text-cyan-300" />
            </motion.div>
          ))}
        </div>
        <div className="relative rounded-lg border border-cyan-300/20 bg-cyan-300/8 p-5">
          <div className="mb-4 flex items-center gap-2 text-sm text-cyan-100">
            <BrainCircuit size={18} />
            {lang === "zh" ? "知识加工管线" : "Knowledge pipeline"}
          </div>
          <div className="space-y-3">
            {t.workflow.map((item, index) => (
              <div key={item} className="flex items-center gap-3">
                <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-white/10 text-xs text-white">{index + 1}</div>
                <div className="h-2 flex-1 rounded-full bg-slate-800">
                  <motion.div
                    initial={{ width: 0 }}
                    animate={{ width: `${45 + index * 8}%` }}
                    transition={{ duration: 0.8, delay: index * 0.08 }}
                    className="h-full rounded-full bg-gradient-to-r from-cyan-300 to-violet-300"
                  />
                </div>
                <span className="w-24 text-right text-xs text-slate-300">{item}</span>
              </div>
            ))}
          </div>
          <div className="mt-6 rounded-md border border-white/10 bg-slate-950/80 p-4">
            <p className="text-xs text-slate-400">MCP</p>
            <p className="mt-1 text-sm text-white">search_memory → ask_memory → get_report</p>
          </div>
        </div>
      </div>
    </div>
  );
}

export default function Home() {
  const [lang, setLang] = useState<Lang>("zh");
  const [theme, setTheme] = useState<Theme>("dark");
  const [themeReady, setThemeReady] = useState(false);
  const [openFaq, setOpenFaq] = useState<number | null>(null);
  const t = copy[lang];
  const isDark = theme === "dark";
  const mcpTools = ["list_topics", "search_memory", "ask_memory", "get_document", "get_report", "import_url", "create_inbox_item"];
  const comparisonRows = useMemo(
    () => [
      ["是否自动结构化", "Auto-structure", ["—", "△", "△", "✓"]],
      ["是否保留原始来源", "Preserve sources", ["△", "✓", "✓", "✓"]],
      ["是否支持混合检索", "Hybrid retrieval", ["—", "△", "✓", "✓"]],
      ["是否支持 RAG 问答", "RAG Q&A", ["—", "△", "✓", "✓"]],
      ["是否支持报告生成", "Report generation", ["—", "—", "△", "✓"]],
      ["是否支持本地优先", "Local-first", ["✓", "✓", "—", "✓"]],
      ["是否支持 Obsidian / Markdown", "Obsidian / Markdown", ["—", "✓", "△", "✓"]],
      ["是否支持 Agent / MCP", "Agent / MCP", ["—", "—", "△", "✓"]]
      , ["多语言自适应处理", "Adaptive multilingual processing", ["—", "△", "△", "✓"]]
      , ["中文检索外文资料", "Search foreign sources in Chinese", ["—", "△", "△", "✓"]]
      , ["中文展示与原文引用", "Chinese presentation with original citations", ["—", "—", "△", "✓"]]
    ],
    []
  );

  useEffect(() => {
    const savedTheme = window.localStorage.getItem("research-memory-theme") as Theme | null;
    if (savedTheme === "dark" || savedTheme === "light") {
      setTheme(savedTheme);
    }
    setThemeReady(true);
  }, []);

  useEffect(() => {
    if (themeReady) {
      window.localStorage.setItem("research-memory-theme", theme);
    }
  }, [theme, themeReady]);

  return (
    <main className={cn("min-h-screen overflow-hidden transition-colors duration-300", isDark ? "theme-dark" : "theme-light")}>
      <header className="sticky top-0 z-50 border-b border-white/8 bg-slate-950/75 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-5 sm:px-8">
          <a href="#top" className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-md bg-cyan-300 text-slate-950">
              <BrainCircuit size={20} />
            </div>
            <div className="leading-tight">
              <p className="text-sm font-semibold text-white">{t.productName}</p>
              <p className="hidden text-xs text-slate-400 sm:block">{t.productEn}</p>
            </div>
          </a>
          <nav className="hidden items-center gap-6 lg:flex">
            {t.nav.map((item, index) => (
              <a key={item} href={["#product", "#features", "#multilingual", "#use-cases", "#local", "#agents", "#download", "#faq"][index]} className="text-sm text-slate-300 hover:text-white">
                {item}
              </a>
            ))}
          </nav>
          <div className="flex items-center gap-2">
            <div className="hidden rounded-md border border-white/10 bg-white/5 p-1 sm:flex">
              <button onClick={() => setLang("zh")} className={cn("rounded px-3 py-1.5 text-xs", lang === "zh" ? "bg-white text-slate-950" : "text-slate-300")}>中文</button>
              <button onClick={() => setLang("en")} className={cn("rounded px-3 py-1.5 text-xs", lang === "en" ? "bg-white text-slate-950" : "text-slate-300")}>English</button>
            </div>
            <button
              type="button"
              onClick={() => setTheme(isDark ? "light" : "dark")}
              aria-label={isDark ? (lang === "zh" ? "切换浅色模式" : "Switch to light mode") : (lang === "zh" ? "切换深色模式" : "Switch to dark mode")}
              title={isDark ? (lang === "zh" ? "切换浅色模式" : "Switch to light mode") : (lang === "zh" ? "切换深色模式" : "Switch to dark mode")}
              className="inline-flex h-10 w-10 items-center justify-center rounded-md border border-white/10 bg-white/5 text-slate-200 transition hover:bg-white/10 hover:text-white"
            >
              {isDark ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <Button variant="ghost" className="hidden xl:inline-flex">{t.demo}</Button>
          </div>
        </div>
      </header>

      <Section id="top" className="pb-10 pt-20">
        <div className="grid items-center gap-10 lg:grid-cols-[0.95fr_1.05fr]">
          <motion.div initial={{ opacity: 0, y: 18 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.6 }}>
            <div className="mb-6 inline-flex items-center gap-2 rounded-md border border-cyan-300/20 bg-cyan-300/10 px-3 py-2 text-sm text-cyan-100">
              <Lock size={16} />
              {t.kicker}
            </div>
            <h1 className="text-balance text-5xl font-semibold tracking-normal text-white md:text-7xl">{t.heroTitle}</h1>
            <p className="mt-6 max-w-2xl text-lg leading-9 text-slate-300">{t.heroSubtitle}</p>
            <div className="mt-5 flex flex-wrap gap-2">
              {(lang === "zh" ? ["多语言资料自适应处理", "中文统一检索", "原文证据可追溯"] : ["Adaptive multilingual processing", "Unified Chinese retrieval", "Traceable original evidence"]).map((badge) => <span key={badge} className="rounded-full border border-cyan-300/20 bg-cyan-300/10 px-3 py-1.5 text-xs text-cyan-100">{badge}</span>)}
            </div>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <DownloadButton lang={lang} className="h-12" />
              <a href="#multilingual" className="inline-flex h-12 items-center justify-center gap-2 rounded-md border border-white/12 bg-white/8 px-5 text-sm font-medium text-white transition hover:bg-white/12">{t.ctaSecondary}<ArrowRight size={17} /></a>
            </div>
            <div className="mt-8 flex flex-wrap items-center gap-2">
              {t.workflow.map((item, index) => (
                <div key={item} className="flex items-center gap-2">
                  <span className="rounded-md border border-white/10 bg-white/5 px-3 py-1.5 text-sm text-slate-200">{item}</span>
                  {index < t.workflow.length - 1 && <ArrowRight size={14} className="text-slate-500" />}
                </div>
              ))}
            </div>
          </motion.div>
          <HeroVisual lang={lang} />
        </div>
      </Section>

      <Section id="product">
        <SectionTitle title={t.painTitle} />
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {pain.map(([zh, en, zhText, enText, Icon]) => (
            <IconCard key={zh} icon={Icon} title={pick(lang, zh, en)} text={pick(lang, zhText, enText)} />
          ))}
        </div>
      </Section>

      <Section>
        <SectionTitle title={t.solutionTitle} text={t.solutionText} />
        <div className="grid gap-5 md:grid-cols-3">
          {valueLayers.map(([zh, en, zhText, enText, Icon], index) => (
            <div key={zh} className="glass interactive-card rounded-lg p-7">
              <div className="mb-8 flex items-center justify-between">
                <div className="flex h-12 w-12 items-center justify-center rounded-md bg-violet-300/12 text-violet-200"><Icon size={22} /></div>
                <span className="text-sm text-slate-500">0{index + 1}</span>
              </div>
              <h3 className="text-xl font-semibold text-white">{pick(lang, zh, en)}</h3>
              <p className="mt-4 text-sm leading-7 text-slate-300">{pick(lang, zhText, enText)}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section id="features">
        <SectionTitle eyebrow={lang === "zh" ? "核心功能" : "Features"} title={t.featuresTitle} />
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {features.map(([zh, en, zhText, enText, Icon]) => (
            <IconCard key={zh} icon={Icon} title={pick(lang, zh, en)} text={pick(lang, zhText, enText)} />
          ))}
        </div>
      </Section>

      <Section id="multilingual">
        <SectionTitle
          eyebrow="MULTILINGUAL KNOWLEDGE"
          title={lang === "zh" ? "外文资料进入，中文知识可用" : "Bring in global sources. Work with them in Chinese."}
          text={lang === "zh" ? "无需在导入前整理语言或手工翻译。系统从文档到分块自动识别语言，建立中文理解层与多路索引；中文内容帮助理解，原始资料负责举证。" : "No pre-sorting or manual translation required. Detect language from document to chunk level, build a Chinese understanding layer and multiple retrieval paths, and keep originals as evidence."}
        />
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          {multilingualStages.map(([zh, en, zhText, enText, Icon], index) => (
            <div key={zh} className="glass interactive-card rounded-lg p-6">
              <div className="flex items-center justify-between"><div className="flex h-11 w-11 items-center justify-center rounded-md bg-violet-300/12 text-violet-200"><Icon size={22} /></div><span className="text-sm text-slate-500">0{index + 1}</span></div>
              <h3 className="mt-6 text-lg font-semibold text-white">{pick(lang, zh, en)}</h3>
              <p className="mt-3 text-sm leading-7 text-slate-300">{pick(lang, zhText, enText)}</p>
            </div>
          ))}
        </div>
        <div className="mt-8 rounded-xl border border-white/10 bg-slate-950/60 p-6 md:p-8">
          <div className="grid items-center gap-6 lg:grid-cols-[1fr_auto_1fr_auto_1fr]">
            {[lang === "zh" ? "资料导入" : "Sources", lang === "zh" ? "语言与内容识别" : "Language detection", lang === "zh" ? "中文索引 + 原文索引" : "Chinese + original indexes"].map((item, index) => <div key={item} className="contents"><div className="rounded-lg border border-cyan-300/20 bg-cyan-300/10 p-5 text-center font-medium text-white">{item}</div>{index < 2 && <ArrowRight className="mx-auto hidden text-cyan-300 lg:block" />}</div>)}
          </div>
        </div>
        <div className="mt-10 text-center"><h3 className="text-2xl font-semibold text-white">{lang === "zh" ? "不是所有资料都需要全文翻译" : "Not every source needs full translation"}</h3></div>
        <div className="mt-6 grid gap-4 md:grid-cols-3">
          {localizationLevels.map(([level, zh, en, zhText, enText, zhBadge, enBadge]) => <div key={level} className="interactive-card rounded-lg border border-white/10 bg-white/[0.04] p-6"><div className="flex items-center justify-between"><span className="font-mono text-cyan-300">{level}</span><span className="rounded-full bg-white/8 px-3 py-1 text-xs text-slate-300">{pick(lang, zhBadge, enBadge)}</span></div><h3 className="mt-5 text-xl font-semibold text-white">{pick(lang, zh, en)}</h3><p className="mt-3 text-sm leading-7 text-slate-300">{pick(lang, zhText, enText)}</p></div>)}
        </div>
        <div className="mt-8 rounded-lg border border-emerald-300/20 bg-emerald-300/10 p-5 text-center text-sm font-medium text-emerald-200">{lang === "zh" ? "中文内容帮助理解，原始资料负责举证。" : "Chinese content supports understanding; original sources provide evidence."}</div>
      </Section>

      <Section id="local">
        <SectionTitle title={t.modesTitle} />
        <div className="grid gap-5 lg:grid-cols-3">
          {modes.map(([zh, en, zhText, enText, Icon]) => (
            <div key={zh} className="interactive-card rounded-lg border border-white/10 bg-slate-950/60 p-7">
              <Icon className="mb-8 text-cyan-300" size={30} />
              <h3 className="text-2xl font-semibold text-white">{pick(lang, zh, en)}</h3>
              <p className="mt-4 text-sm leading-7 text-slate-300">{pick(lang, zhText, enText)}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section>
        <SectionTitle title={t.howTitle} />
        <div className="relative grid gap-4 md:grid-cols-2 lg:grid-cols-7">
          {steps.map(([zh, en, zhText, enText], index) => (
            <div key={zh} className="interactive-card rounded-lg border border-white/10 bg-white/[0.04] p-5">
              <div className="mb-5 flex h-9 w-9 items-center justify-center rounded-md bg-cyan-300 text-sm font-semibold text-slate-950">{index + 1}</div>
              <h3 className="text-base font-semibold text-white">{pick(lang, zh, en)}</h3>
              <p className="mt-3 text-sm leading-6 text-slate-400">{pick(lang, zhText, enText)}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section id="use-cases">
        <SectionTitle title={t.useTitle} />
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {useCases.map(([zh, en, zhText, enText, Icon]) => (
            <IconCard key={zh} icon={Icon} title={pick(lang, zh, en)} text={pick(lang, zhText, enText)} />
          ))}
        </div>
      </Section>

      <Section>
        <div className="grid gap-8 rounded-lg border border-cyan-300/15 bg-cyan-300/[0.05] p-8 lg:grid-cols-[0.9fr_1.1fr] lg:p-12">
          <div>
            <p className="mb-3 text-sm font-medium text-cyan-300">{lang === "zh" ? "隐私与控制" : "Privacy and control"}</p>
            <h2 className="text-3xl font-semibold text-white md:text-5xl">{t.privacyTitle}</h2>
            <p className="mt-5 text-base leading-8 text-slate-300">{t.privacyText}</p>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            {trust.map(([zh, en, Icon]) => (
              <div key={zh} className="interactive-card rounded-lg border border-white/10 bg-slate-950/60 p-5">
                <Icon className="mb-5 text-emerald-300" size={24} />
                <p className="text-sm font-medium text-white">{pick(lang, zh, en)}</p>
              </div>
            ))}
          </div>
        </div>
      </Section>

      <Section id="agents">
        <div className="grid gap-8 lg:grid-cols-[0.9fr_1.1fr]">
          <div>
            <SectionTitle eyebrow="MCP / Agent" title={t.agentTitle} text={t.agentText} />
          </div>
          <div className="glass rounded-lg p-6">
            <div className="mb-5 flex items-center gap-3">
              <TerminalSquare className="text-cyan-300" />
              <p className="font-medium text-white">Local MCP Server</p>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {mcpTools.map((tool) => (
                <code key={tool} className="rounded-md border border-white/10 bg-black/30 px-4 py-3 text-sm text-cyan-100">
                  {tool}
                </code>
              ))}
            </div>
          </div>
        </div>
      </Section>

      <Section>
        <SectionTitle title={t.compareTitle} />
        <div className="overflow-x-auto rounded-lg border border-white/10">
          <table className="w-full min-w-[780px] border-collapse bg-slate-950/70 text-sm">
            <thead>
              <tr className="border-b border-white/10 text-left text-slate-300">
                <th className="p-4">{lang === "zh" ? "能力" : "Capability"}</th>
                {["普通收藏夹", "传统笔记软件", "云端知识库", "Memorix"].map((h, i) => (
                  <th key={h} className={cn("p-4", i === 3 && "text-cyan-200")}>
                    {lang === "zh" ? h : ["Bookmarks", "Traditional notes", "Cloud knowledge base", "Memorix"][i]}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {comparisonRows.map(([zh, en, values]) => (
                <tr key={zh as string} className="border-b border-white/8 last:border-0">
                  <td className="p-4 text-white">{pick(lang, zh as string, en as string)}</td>
                  {(values as string[]).map((value, index) => (
                    <td key={index} className={cn("p-4 text-slate-300", value === "✓" && "text-emerald-300", index === 3 && "bg-cyan-300/5 font-medium")}>
                      {value}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Section>

      <Section id="download">
        <div className="rounded-lg border border-white/10 bg-gradient-to-br from-cyan-300/12 via-violet-300/10 to-white/[0.03] p-8 text-center md:p-14">
          <h2 className="text-balance text-3xl font-semibold text-white md:text-5xl">{t.ctaTitle}</h2>
          <p className="mx-auto mt-5 max-w-2xl text-base leading-8 text-slate-300">
            {lang === "zh" ? "把全球资料沉淀为可用中文工作的长期知识资产。系统会推荐适合当前设备的版本，你也可以查看全部下载。" : "Turn global sources into long-term knowledge assets you can work with in Chinese. We recommend a build for your device, while keeping every available download visible."}
          </p>
          <div className="mt-8 flex flex-col justify-center gap-3 sm:flex-row">
            <DownloadButton lang={lang} className="h-12" />
            <a href="#multilingual" className="inline-flex h-12 items-center justify-center rounded-md border border-white/12 bg-white/8 px-5 text-sm font-medium text-white hover:bg-white/12">{lang === "zh" ? "了解多语言工作流" : "Explore multilingual workflow"}</a>
          </div>
        </div>
      </Section>

      <Section id="faq">
        <SectionTitle title="FAQ" />
        <div className="mx-auto max-w-4xl divide-y divide-white/10 rounded-lg border border-white/10 bg-slate-950/60">
          {faq.map(([zhQ, enQ, zhA, enA], index) => (
            <details
              key={zhQ}
              open={openFaq === index}
              className="group p-5"
            >
              <summary
                onClick={(event) => {
                  event.preventDefault();
                  setOpenFaq(openFaq === index ? null : index);
                }}
                className="flex cursor-pointer list-none items-center justify-between gap-4 text-base font-medium text-white"
              >
                {pick(lang, zhQ, enQ)}
                <ChevronDown className="shrink-0 text-slate-400 transition group-open:rotate-180" size={18} />
              </summary>
              <p className="mt-4 text-sm leading-7 text-slate-300">{pick(lang, zhA, enA)}</p>
            </details>
          ))}
        </div>
      </Section>

      <footer className="border-t border-white/10 px-5 py-10 text-center text-sm text-slate-500">
        <p>{t.productName} · {t.productEn} · HiqerTech</p>
      </footer>
    </main>
  );
}
