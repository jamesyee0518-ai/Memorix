# Memorix — Personal AI Memory Engine

A local-first AI knowledge asset engine that transforms scattered information into a searchable, reusable, and agent-accessible personal memory system.

Memorix helps researchers, developers, creators, and professionals capture information from the web, documents, images, conversations, and mobile inputs — then automatically process, structure, retrieve, and reuse that knowledge with AI.

> Your personal AI memory layer.

---

# Overview

Modern professionals consume massive amounts of information every day:

* Web articles
* Research papers
* PDFs
* Meeting notes
* Screenshots
* Chat messages
* Audio recordings
* Project documents

However, most information remains trapped in:

* Browser bookmarks
* Chat histories
* File folders
* Note-taking apps
* Personal memory

Memorix solves this problem by turning fragmented information into structured knowledge assets.

It provides:

* AI-powered content processing
* Hybrid search
* RAG-based knowledge Q&A
* Automated research reports
* Markdown / Obsidian export
* Local AI model support
* MCP / Agent integration

---

# Core Philosophy

## Local First

Your knowledge belongs to you.

Memorix prioritizes:

* Local data storage
* Private knowledge processing
* Local AI models
* User-controlled synchronization
* Open data formats

Sensitive information can stay on your device.

---

## Cloud Optional

Cloud capabilities are available when needed.

Users can choose:

### Local Workspace

Designed for:

* Privacy-focused users
* Developers
* Researchers
* Local AI enthusiasts

Features:

* SQLite storage
* Local Vault
* Ollama / LM Studio support
* Local MCP Server
* Offline-first workflow

### Cloud Workspace

Designed for:

* Multi-device access
* Teams
* Cloud processing

Features:

* PostgreSQL + pgvector
* Cloud storage
* Web access
* Collaboration-ready architecture

### Hybrid Workspace

The best balance between privacy and convenience.

Example workflow:

```text
Mobile Capture
      ↓
Cloud Inbox
      ↓
Desktop Sync
      ↓
Local Knowledge Engine
      ↓
RAG / Agent / Reports
```

---

# Key Features

## 📥 Intelligent Knowledge Capture

Capture information from multiple sources:

* Web URLs
* PDF files
* Markdown
* Text notes
* Images
* Audio
* Mobile chat input

All incoming information enters an Inbox layer before becoming permanent knowledge.

Benefits:

* Avoid knowledge pollution
* Classify information automatically
* Recommend topics
* Retry failed processing

---

# 🧠 AI Knowledge Processing

Memorix transforms raw information into structured knowledge.

Processing pipeline:

```text
Capture
  ↓
Parse
  ↓
Clean
  ↓
Normalize
  ↓
Summarize
  ↓
Extract Entities
  ↓
Generate Tags
  ↓
Create Embeddings
  ↓
Index
  ↓
Knowledge Ready
```

AI automatically generates:

* Summaries
* Key insights
* One-sentence conclusions
* Tags
* Entities
* Relationships
* Value scores
* Reusable knowledge fragments

---

# 🔍 Hybrid Search & RAG

Memorix combines multiple retrieval strategies:

* Keyword search
* Vector search
* Tag filtering
* Entity filtering
* Time filtering
* Source filtering
* Value ranking

Example questions:

> "What are the latest AI Agent trends?"

> "Find all research related to GraphRAG."

> "Summarize this week's important AI industry changes."

Answers are generated with:

* Relevant context
* Source references
* Traceable evidence

---

# 📊 AI Report Generation

Turn knowledge into actionable outputs.

Supported reports:

* Daily briefs
* Weekly summaries
* Topic research reports
* Competitive analysis
* Writing materials

Example:

```text
AI Research Topic
        ↓
Collected Sources
        ↓
AI Analysis
        ↓
Research Report
        ↓
Markdown / Obsidian Export
```

---

# 📱 Mobile Knowledge Capture

The mobile app is designed as an information entry point, not a traditional note-taking application.

Users can:

* Send URLs
* Write ideas
* Upload images
* Upload documents
* Record audio

Example:

```
User:
Save this article for AI research.

Memorix:
Received.
Added to Inbox.
Suggested topic:
"AI Agent Industry Research"
```

---

# 🗂 Open Knowledge Format

Your knowledge should remain portable.

Memorix supports:

* Markdown
* Obsidian Vault
* JSON export

Example:

```text
KnowledgeVault/

  topics/

    AI Research/

      documents/

      reports/

      attachments/

  exports/
```

No vendor lock-in.

---

# 🤖 Agent & MCP Integration

Memorix turns your knowledge base into long-term memory for AI agents.

Supported integrations:

* Claude
* Cursor
* Hermes
* Local AI Agents

Through MCP:

```text
AI Agent
    ↓
MCP Server
    ↓
Memorix Memory Engine
    ↓
Knowledge Base
```

Available tools:

```text
list_topics

search_memory

ask_memory

get_document

get_report

import_url

create_inbox_item
```

Agents can:

* Search your private knowledge
* Retrieve documents
* Generate reports
* Use your accumulated experience

---

# Architecture

```text
                 User

                  |
                  ↓

     Desktop / Web / Mobile Interface

                  |
                  ↓

          Workspace Router

          /                 \

 Local Runtime              Cloud Runtime

 SQLite                     PostgreSQL
 Local Vault                S3 / MinIO
 Local Queue                Redis Worker
 Local Vector Store         pgvector
 Ollama / LM Studio         Cloud Models
 Local MCP                  Agent API


                  |
                  ↓

             Knowledge Engine

                  |
                  ↓

             AI Agents
```

---

# Technology Stack

## Desktop

* Tauri
* React / Vue
* TypeScript
* Tailwind CSS
* shadcn/ui

## Local Runtime

* SQLite
* Local Vault
* Ollama
* LM Studio
* sqlite-vec / LanceDB

## Cloud Runtime

* ASP.NET Core / .NET
* PostgreSQL
* pgvector
* Redis
* MinIO / S3

## AI Layer

Supports:

* OpenAI-compatible APIs
* Anthropic
* Gemini
* DeepSeek
* Ollama
* LM Studio

## Agent Layer

* MCP Server
* Local Agent Bridge
* Cloud Agent API

---

# Use Cases

## AI Research Assistant

Collect:

* AI news
* Papers
* Technical blogs
* Company updates

Generate:

* Daily AI briefings
* Trend analysis
* Research reports

## Software Development Memory

Store:

* Architecture documents
* Code decisions
* Technical notes
* Project history

Ask:

> "Why was this architecture chosen?"

> "How does this module work?"

## Product Research

Build knowledge bases for:

* Competitor analysis
* Market research
* Product strategy

## Personal Knowledge System

Create a long-term AI memory containing:

* Ideas
* Learning notes
* Research materials
* Work experience

---

# Roadmap

## Phase 1 — Local AI Memory Foundation

* Tauri desktop application
* SQLite knowledge store
* Local Vault
* URL/PDF/Text import
* AI summarization

## Phase 2 — Knowledge Intelligence

* Tags
* Entities
* Chunking
* Embeddings
* Hybrid search
* RAG Q&A

## Phase 3 — Output & Integration

* Reports
* Markdown export
* Obsidian export
* MCP Server

## Phase 4 — Cloud & Mobile

* Cloud Inbox
* Mobile capture
* Synchronization
* Team workspace

---

# Design Principles

## Privacy by Default

Keep personal knowledge under user control.

## Open Data

Markdown and exportable formats.

## Agent Ready

Knowledge should be accessible by AI systems.

## Human Controlled Automation

AI assists decisions but does not silently modify user data.

## Extensible Architecture

Local, cloud, and hybrid modes share the same knowledge model.

---

# Vision

Memorix is building a new category of personal AI infrastructure:

Not another note-taking app.

Not another chatbot.

A personal AI memory layer that grows with you.

Your knowledge.
Your experience.
Your AI memory.

---

# License

TBD
