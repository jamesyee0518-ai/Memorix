# Memorix Scripts

## API smoke test

Run the backend dependency services and API first:

```bash
docker compose up -d postgres minio
dotnet run --project src/KnowledgeEngine.Api/KnowledgeEngine.Api.csproj --launch-profile http
```

Then run:

```bash
node scripts/smoke-api.mjs
```

The default smoke path verifies health, registration, authentication, topic creation,
text import, source detail, and source listing. It intentionally avoids LLM-dependent
processing so it can run on a clean local machine.

To include processing and keyword search:

```bash
SMOKE_RUN_PROCESSING=1 node scripts/smoke-api.mjs
```

Optional environment variables:

- `API_BASE_URL`: API root URL. Default: `http://localhost:9101`.
- `SMOKE_TIMEOUT_MS`: per-request timeout in milliseconds. Default: `10000`.
- `SMOKE_RUN_PROCESSING`: set to `1` to trigger document processing and search.

Troubleshooting:

- After code changes, stop any old API process before starting a new one.
- If the database was created while an older API failed startup, reset local data with
  `docker compose down -v` and then start `postgres` and `minio` again.
- `/health` checks that the database schema is reachable. If it returns `unhealthy`,
  restart the API and inspect the startup error before running this smoke test.
