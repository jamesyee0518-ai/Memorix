# Memorix

A local-first AI knowledge asset engine that turns scattered information into searchable, explainable, and agent-ready memory.

Memorix captures web pages, PDFs, images, audio recordings, notes, and chat inputs, then runs them through an AI processing pipeline that cleans, summarizes, structures, indexes, and makes them retrievable. It supports multilingual sources with a Chinese understanding layer, hybrid retrieval with evidence tracing, automatic report generation, and exposure of the knowledge base to external agents through a local MCP server.

## Key Features

- **Multi-source capture** — Import URLs, PDFs, Markdown, plain text, images, audio, and mobile chat-style inputs into a unified Inbox.
- **AI processing pipeline** — Automatic content cleaning, summarization, key-point extraction, tagging, entity recognition, risk/opportunity detection, and value scoring.
- **Adaptive multilingual processing** — Detects language at document and chunk level, builds Chinese titles, summaries, keywords, and entities for foreign-language sources while preserving originals.
- **Hybrid retrieval** — Combines Chinese full-text, keyword, vector, cross-language semantic, terminology, entity, time, and value-score signals so a Chinese query can recall both Chinese and foreign-language sources.
- **RAG Q&A with evidence tracing** — Answers are presented in Chinese with citations that link back to the original document, section, or page, including foreign-language source text.
- **Automatic reports** — Generate daily briefs, weekly reports, topic research reports, competitor analyses, and writing materials.
- **Three workspace modes** — Local (SQLite + local Vault), Cloud (PostgreSQL + object storage), and Hybrid (local knowledge base with mobile capture synced through a cloud Inbox).
- **Local and cloud models** — Supports Ollama, LM Studio, local models, and any OpenAI-compatible API endpoint.
- **Voice capabilities** — Speech-to-text via Whisper.cpp, FunASR, and Faster-Whisper; text-to-speech via Fish Speech, Piper, and system TTS. Includes VAD, diarization, post-ASR correction, and a DAG-based transcription pipeline.
- **MCP / Agent integration** — Expose the knowledge base to Claude, Cursor, Hermes, and local automation scripts through a local MCP server with tools like `search_memory`, `ask_memory`, `get_document`, and `get_report`.
- **Entity resolution and knowledge graph** — Deduplication, alias linking, vector-similarity candidate generation, LLM disambiguation, and entity relationship graphing.
- **Billing and payments** — Token-based metering with shadow pricing, credit system, and WeChat Pay / Alipay integration.
- **Open export** — Markdown and Obsidian Vault export to avoid data lock-in.

## Architecture

Memorix follows a clean architecture pattern on the backend with four .NET 10 projects, wrapped by a Tauri desktop shell, an Expo mobile app, and a Next.js landing page.

### Backend — Knowledge Engine (.NET 10 / ASP.NET Core)

| Layer | Project | Responsibility |
|---|---|---|
| Domain | `KnowledgeEngine.Domain` | 80+ entities (`Source`, `Document`, `DocumentChunk`, `Entity`, `Topic`, `Report`, `AudioAsset`, `TranscriptionJob`, billing and payment entities, etc.) and enums. No external dependencies. |
| Application | `KnowledgeEngine.Application` | DTOs, 40+ service interfaces (`ISearchService`, `IEmbeddingService`, `IAsrProvider`, `ITtsProvider`, `IPipelineNode`, etc.), application services, mapping, and DI registration. |
| Infrastructure | `KnowledgeEngine.Infrastructure` | EF Core with PostgreSQL+pgvector and SQLite, MinIO/S3 object storage, vector stores, document processing pipeline, DAG pipeline engine, audio providers, entity resolution, search/QA, MCP server, agent tools, billing, payments, and reports. |
| API | `KnowledgeEngine.Api` | 60+ REST controllers, SignalR hubs (`TranscriptionHub`, `TtsHub`), JWT authentication, middleware (error handling, trace ID, agent auth, cloud API proxy), Swagger/OpenAPI. |

### Desktop — Tauri 2 + Rust

Cross-platform desktop application (`desktop/`) built with Tauri v2. The Rust layer (`src-tauri/`) manages the embedded .NET API runtime and window lifecycle. The web shell loads a local frontend. Supports auto-updates via signed manifests. Build targets include macOS (Apple Silicon) and Windows x64.

### Mobile — Expo / React Native

The mobile app (`mobile/`) is built with Expo SDK 53 and React Native 0.79. It focuses on capture — chat-style input, file and document picking, audio recording, and push notifications — feeding into the cloud Inbox for later processing on desktop. Includes offline queue and local auth storage.

### Landing Page — Next.js

The marketing site (`preview/`) is a Next.js application with Tailwind CSS and Framer Motion. Bilingual (Chinese/English) with dark and light themes.

## Tech Stack

| Area | Technologies |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core 10, Serilog, Swagger, SignalR |
| Database | PostgreSQL 16 with pgvector (cloud), SQLite (local) |
| Object storage | MinIO (S3-compatible), local file system |
| Caching | Redis 7 |
| Desktop | Tauri 2, Rust |
| Mobile | Expo SDK 53, React Native 0.79, React 19 |
| Web | Next.js 15, React 19, Tailwind CSS, Framer Motion |
| AI / LLM | OpenAI-compatible APIs, Ollama, LM Studio |
| Audio / ASR | Whisper.cpp, FunASR, Faster-Whisper, Fish Speech, Piper |
| Payments | WeChat Pay, Alipay |
| Infrastructure | Docker, Docker Compose |

## Project Structure

```
Memorix/
├── src/                              # .NET backend solution
│   ├── KnowledgeEngine.Api/          # ASP.NET Core Web API (controllers, hubs, middleware)
│   ├── KnowledgeEngine.Application/  # DTOs, interfaces, application services
│   ├── KnowledgeEngine.Domain/       # Domain entities and enums
│   └── KnowledgeEngine.Infrastructure/ # EF Core, storage, processing, search, audio, MCP
├── desktop/                          # Tauri desktop app
│   ├── src-tauri/                    # Rust backend (runtime coordination, window, updater)
│   ├── shell/                        # Web frontend shell
│   └── scripts/                      # Build, version check, updater manifest scripts
├── mobile/                           # Expo / React Native mobile app
├── preview/                          # Next.js landing page
├── scripts/                          # Migrations, build helpers, smoke tests
│   └── migrations/                   # SQL migrations (PostgreSQL + SQLite)
├── deploy/                           # Deployment configs (nginx, web.config, IIS)
├── doc/                              # Development plans and design documents
├── docs/                             # Architecture and competitive analysis docs
├── docker-compose.yml                # Dev infrastructure (Postgres, Redis, MinIO, audio services)
└── .github/workflows/                # CI/CD (desktop build)
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+ and npm
- [Rust](https://www.rust-lang.org/tools/install) (for desktop builds)
- [Docker](https://www.docker.com/) and Docker Compose (for infrastructure services)

### Start Infrastructure Services

PostgreSQL (with pgvector), Redis, and MinIO are defined in `docker-compose.yml`. Audio services (FunASR, Fish Speech, Piper) run under the `audio` profile.

```bash
docker compose up -d                    # core services: postgres, redis, minio
docker compose --profile audio up -d    # also start ASR/TTS services (optional)
```

### Run the Backend API

```bash
cd src/KnowledgeEngine.Api
dotnet restore
dotnet run
```

The API launches with Swagger UI at `http://localhost:9101` (or the port configured in `Properties/launchSettings.json`). Database migrations are applied automatically on startup for SQLite; for PostgreSQL, run the SQL scripts in `scripts/migrations/`.

### Run the Desktop App

```bash
cd desktop
npm install
npm run dev          # starts Tauri dev mode with the embedded API
```

The desktop app bundles the .NET API as a sidecar process. The `beforeDevCommand` hook starts local services, and the window loads from `http://localhost:3000` during development.

### Run the Mobile App

```bash
cd mobile
npm install
npx expo start       # opens Expo DevTools, scan QR to launch on device/simulator
```

### Run the Landing Page

```bash
cd preview
npm install
npm run dev          # Next.js dev server
```

## Configuration

The primary configuration file is `src/KnowledgeEngine.Api/appsettings.json`. Key sections:

| Section | Purpose |
|---|---|
| `ConnectionStrings` | PostgreSQL connection string (cloud mode) |
| `Jwt` | JWT issuer, audience, secret, and token expiry |
| `Minio` | Object storage endpoint, credentials, and bucket |
| `Llm` | LLM endpoint, API key, model name, and max tokens (OpenAI-compatible) |
| `Embedding` | Embedding model endpoint and model name |
| `Audio` | ASR/TTS provider toggles, Whisper model, VAD, device capability thresholds, LAN node discovery, circuit breaker, and provider pricing |
| `Billing` | Token metering rates, credit system, entitlement enforcement, and meter definitions |
| `Payment` | WeChat Pay and Alipay credentials, product/credit packages, and order settings |
| `EntityResolution` | Entity deduplication, auto-link, vector candidates, LLM disambiguation, and graph backend toggles |
| `Features` | Feature flags for desktop cloud mode, hybrid mode, and cloud Inbox |
| `LocalFileStorage` | Vault root directory for local file storage |
| `Cors` | Allowed CORS origins |

Environment-specific overrides go in `appsettings.Development.json`. Secrets should be managed via environment variables or user secrets, not committed to the repository.

## Deployment

### Desktop Distribution

Desktop builds are produced via Tauri's bundler and distributed through the auto-update manifest hosted at `https://memorix.hiqer.top/desktop-updates/stable/latest.json`. The GitHub Actions workflow in `.github/workflows/desktop-build.yml` handles CI builds. Build artifacts include:

- macOS: `.dmg` (Apple Silicon)
- Windows: `.msi` / `.exe` (x64)

```bash
cd desktop
npm run build              # tauri build (debug)
npm run build:update       # tauri build with release updater config
```

### Server Deployment

For cloud-mode deployments, the .NET API can be published and hosted behind IIS or nginx. Deployment guides are in the `doc/` and `deploy/` directories, including IIS site setup, nginx configuration, and `web.config` for Windows Server.

```bash
cd src/KnowledgeEngine.Api
dotnet publish -c Release -o ./publish
```

### Database Migrations

SQL migration scripts live in `scripts/migrations/` with separate files for PostgreSQL and SQLite. Apply them in chronological order:

```bash
# PostgreSQL example
psql -h localhost -U ke_user -d knowledge_engine -f scripts/migrations/20260713_topic_summary_templates.postgres.sql
psql -h localhost -U ke_user -d knowledge_engine -f scripts/migrations/20260801_audio_capability_tables.postgres.sql
```

## Workspace Modes

| Mode | Data Location | Best For |
|---|---|---|
| Local | SQLite + local Vault on the user's machine | Privacy-sensitive research, developer workflows, offline use |
| Cloud | PostgreSQL + object storage on a server | Multi-device access, lightweight users, team collaboration |
| Hybrid | Local knowledge base + cloud Inbox for mobile capture | Balancing privacy with mobile convenience |

The runtime router (`RuntimeRouter`) transparently switches between local and cloud repositories based on the workspace binding, so the same API surface serves all three modes.

## MCP / Agent Integration

Memorix exposes a local MCP server (`KnowledgeEngine.Infrastructure/Mcp/McpServer.cs`) that allows external agents to interact with the knowledge base within authorized scopes. Available tools include:

- `list_topics` — List knowledge base topics
- `search_memory` — Hybrid search across documents and chunks
- `ask_memory` — RAG Q&A with evidence citations
- `get_document` — Retrieve a specific document
- `get_report` — Generate or retrieve a report
- `import_url` — Import a web page into the Inbox
- `create_inbox_item` — Create a new Inbox item

Agent access is gated by API keys and workspace authorization. The `AgentPermissionGuard` enforces per-agent scopes.

## License

This project is proprietary software developed by HiqerTech.
