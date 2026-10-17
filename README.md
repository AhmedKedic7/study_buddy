# Study Buddy

An agentic RAG study assistant. Upload lecture PDFs, then ask questions answered **only from your notes** (with page citations), or have it generate **interactive quizzes** on any topic.

Built for a pharmacy student, where getting drug facts right matters, so every answer is grounded in the uploaded material and cites its sources.

## How it works

```
Next.js (Vercel)  ──SSE──▶  ASP.NET Core API  ──▶  LLM on Groq (tool-use agent loop)
                                  │                    │
                                  │                    ├─ search_notes  ─▶ pgvector similarity search
                                  │                    └─ generate_quiz ─▶ retrieval + structured-output quiz
                                  │
             upload PDF ─▶ background worker: PdfPig → chunk (per page) → Voyage embeddings → Postgres/pgvector
```

- **Agent loop**: the model decides when to search the notes (and re-search with different wording), when to build a quiz, and when it has enough to answer. Each step streams to the UI live ("Searching your notes for…").
- **Grounded answers**: chunks never cross page boundaries, so every `[n]` citation maps to an exact document and page.
- **Quizzes**: a second LLM call with a strict JSON schema (structured outputs) turns retrieved passages into multiple-choice questions, rendered as interactive cards with explanations.
- **Provider-agnostic LLM layer**: the agent is written against `IChatClient` (Microsoft.Extensions.AI). Groq is the default; any OpenAI-compatible API (OpenAI, OpenRouter, a local Ollama) works by changing `Llm:BaseUrl` and `Llm:Model`.

## Tech stack

| Layer | Tech |
|---|---|
| Frontend | Next.js 16, React 19, Tailwind CSS 4, react-markdown |
| Backend | ASP.NET Core (.NET 10) minimal APIs, EF Core, background worker (`Channel<T>`) |
| LLM | `openai/gpt-oss-120b` on [Groq](https://console.groq.com) (free tier), via Microsoft.Extensions.AI + the OpenAI .NET SDK |
| Embeddings | Voyage AI `voyage-3.5` (1024-dim) |
| Vector DB | PostgreSQL + pgvector (HNSW index, cosine distance) |
| PDF parsing | PdfPig |

## Running locally

**Prerequisites:** .NET 10 SDK, Node 20+, Docker, a [Groq API key](https://console.groq.com/keys) (free) and a [Voyage AI API key](https://dash.voyageai.com) (the free tier is plenty).

```bash
# 1. Configuration: add your two API keys to .env (gitignored)
cp .env.example .env

# 2. Database (pgvector on localhost:5433)
docker compose up -d db

# 3. Backend (http://localhost:5000)
cd backend/StudyBuddy.Api
dotnet run --launch-profile http

# 4. Frontend (http://localhost:3000), in a second terminal
cd frontend
cp .env.example .env.local
npm install
npm run dev
```

Or run the database and backend together in Docker: `docker compose --profile full up --build`.

The database schema is created automatically on first start.

## Configuration

All backend settings live in [`.env.example`](.env.example) with comments. Copy it to `.env` and fill in the required values. The backend loads `.env` on startup (searching up from its working directory), and docker compose reads the same file.

Variable names follow .NET's configuration convention (`Llm__ApiKey` maps to `Llm:ApiKey`), so the same names work as plain environment variables on any host. Real environment variables take precedence over `.env`, and anything unset falls back to `appsettings.json`.

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `Llm__ApiKey` | ✅ | | Groq API key |
| `Voyage__ApiKey` | ✅ | | Voyage AI API key |
| `Llm__BaseUrl` | | `https://api.groq.com/openai/v1` | Any OpenAI-compatible chat API |
| `Llm__Model` | | `openai/gpt-oss-120b` | Chat model (must support tool calling) |
| `Voyage__BaseUrl` | | `https://api.voyageai.com/v1` | Embeddings endpoint |
| `Voyage__Model` | | `voyage-3.5` | Embedding model (must output 1024 dimensions) |
| `ConnectionStrings__Postgres` | | local compose database | PostgreSQL with pgvector |
| `Cors__Origins__0` | | `http://localhost:3000` | Frontend URL allowed to call the API |
| `Storage__UploadPath` | | `data/uploads` | Where uploaded PDFs are stored |

Frontend (`frontend/.env.local`, see [`frontend/.env.example`](frontend/.env.example)):

| Variable | Required | Purpose |
|---|---|---|
| `NEXT_PUBLIC_API_URL` | ✅ | Backend URL, e.g. `http://localhost:5000` |

> **Free-tier limits:** Groq allows about 8,000 tokens per minute for `gpt-oss-120b`, so a few rapid questions in a row can hit the limit; the app shows a "wait a minute" message when that happens. For faster demos switch `Llm:Model` to `openai/gpt-oss-20b` (same limits, quicker), or upgrade to Groq's Developer plan.

## Deploying

| Piece | Suggested host | Notes |
|---|---|---|
| Database | [Neon](https://neon.tech) or [Supabase](https://supabase.com) | Both support pgvector on the free tier |
| API | Render, Railway, Fly.io or Azure Container Apps | Deploy `backend/Dockerfile` and attach a persistent volume for uploads |
| Frontend | Vercel | Set `NEXT_PUBLIC_API_URL` to the API URL |

In production, don't upload a `.env` file. Set the same variables (see [Configuration](#configuration)) in your host's environment settings:

```
ConnectionStrings__Postgres=Host=...;Database=...;Username=...;Password=...;SSL Mode=Require
Llm__ApiKey=...
Voyage__ApiKey=...
Cors__Origins__0=https://your-app.vercel.app
Storage__UploadPath=/data/uploads
```

## API

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/documents` | List documents and their processing status |
| `POST` | `/api/documents` | Upload a PDF (multipart, field `file`) |
| `DELETE` | `/api/documents/{id}` | Delete a document and its chunks |
| `POST` | `/api/chat` | `{ messages, documentIds? }` → SSE stream of `step`, `sources`, `quiz`, `answer` and `error` events |

## Roadmap

- [ ] Auth, so each user's documents are private
- [ ] Hybrid search (pgvector + Postgres full-text) and re-ranking
- [ ] OCR for scanned PDFs (e.g. Tesseract, or a vision-capable model)
- [ ] Flashcards with spaced repetition, plus tracking weak topics across sessions
- [ ] `drug_card` tool: a structured drug summary (class, mechanism, indications, adverse effects, interactions)
- [ ] Evaluation suite: a question set with retrieval hit-rate and answer-faithfulness scores
- [ ] Token-by-token streaming of the final answer (`GetStreamingResponseAsync`)
- [ ] Rate limiting and a daily spend cap for the public demo
