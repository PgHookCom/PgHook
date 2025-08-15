# PgHook

PgHook streams PostgreSQL change events (logical replication via [PgOutput2Json](https://github.com/PgOutput2Json/PgOutput2Json)) and delivers them to a **webhook**.

Distributed as a small-footprint 23.17 MB container image (AOT-compiled .NET 9, Alpine).

---

## Quick start (Docker)

1) To enable logical replication, add the following setting in your `postgresql.conf`:

```
wal_level = logical

# If needed increase the number of WAL senders, replication slots.
# The default is 10 for both.
max_wal_senders = 10
max_replication_slots = 10
```

Other necessary settings usually have appropriate default values for a basic setup.

> **Note:** PostgreSQL must be **restarted** after modifying this setting.

2) Ensure PostgreSQL has a publication for the tables you want to watch:
```sql
CREATE PUBLICATION mypub FOR TABLE table1, table2;
```

3) Run PgHook with the minimum required environment:
```bash
docker run --rm \
  -e PGH_POSTGRES_CONN="Host=mydbserver;Username=replicator;Password=secret;Database=mydb;ApplicationName=PgHook" \
  -e PGH_PUBLICATION_NAMES="mypub" \
  -e PGH_WEBHOOK_URL="https://example.com/webhooks/pghook" \
  pghook/pghook
```

That’s it. As rows change, PgHook will POST events to your webhook.

> Want a stable replication position across restarts?
> Add a permanent slot: `-e PGH_USE_PERMANENT_SLOT=true -e PGH_REPLICATION_SLOT=myslot`  
> (if omitted, PgHook generates a temporary slot name).

---

## Webhook payload

Each change is a compact JSON object (from [PgOutput2Json](https://github.com/PgOutput2Json/PgOutput2Json)). Batches are delivered to your webhook (up to `PGH_BATCH_SIZE` items per POST). Each element looks like:

```jsonc
{
  "c": "U",             // Change type: I (insert), U (update), D (delete)
  "w": 2485645760,      // Deduplication key (derived from WAL start)
  "t": "schema.table",  // Table name
  "k": { /* key or old values (depends on table/replica identity) */ },
  "r": { /* new row values; not present for deletes */ }
}
```

### Signatures (optional)
If you set `PGH_WEBHOOK_SECRET`, each request includes:
- `X-Hub-Signature-256`: HMAC-SHA256 of the request body using your secret
- `X-Timestamp`: Unix timestamp of when the payload was signed

Use these to verify authenticity and freshness on the receiver.

---

## Configuration

All configuration is via environment variables. Required ones first; everything else is optional.

| Variable | Type | Default | Description |
|---|---:|---|---|---|
| `PGH_POSTGRES_CONN` | string | — | PostgreSQL connection string (Npgsql format). |
| `PGH_PUBLICATION_NAMES` | string | — | Comma-separated publication name(s) to subscribe to. |
| `PGH_WEBHOOK_URL` | string (URL) | — | Webhook endpoint that will receive change batches via HTTP POST. |
| `PGH_REPLICATION_SLOT` | string | Auto-generated when not using permanent slot | Replication slot name. Required if `PGH_USE_PERMANENT_SLOT=true`. |
| `PGH_USE_PERMANENT_SLOT` | bool | `false` | Use a permanent logical replication slot instead of a temporary one. |
| `PGH_BATCH_SIZE` | int | `100` | Max number of change events per POST. |
| `PGH_JSON_COMPACT` | bool | `false` | Emit compact JSON (minified). |
| `PGH_WEBHOOK_SECRET` | string | `""` | If set, requests are signed (see **Signatures**). |
| `PGH_WEBHOOK_TIMEOUT_SEC` | int (sec) | `30` | Overall HTTP request timeout. |
| `PGH_WEBHOOK_CONNECT_TIMEOUT_SEC` | int (sec) | `10` | Connect timeout for the HTTP client. |
| `PGH_WEBHOOK_KEEPALIVE_DELAY_SEC` | int (sec) | `60` | TCP keep-alive probe delay. |
| `PGH_WEBHOOK_KEEPALIVE_TIMEOUT_SEC` | int (sec) | `10` | TCP keep-alive probe timeout. |
| `PGH_WEBHOOK_POOLED_CONNECTION_LIFETIME_SEC` | int (sec) | 600 | Max lifetime for pooled HTTP connections. |
| `PGH_WEBHOOK_POOLED_CONNECTION_IDLE_TIMEOUT_SEC` | int (sec) | 120 | Idle timeout for pooled HTTP connections. |

> Notes
> - `PGH_PUBLICATION_NAMES` can list multiple publications separated by commas.
> - If `PGH_REPLICATION_SLOT` isn’t supplied and `PGH_USE_PERMANENT_SLOT=false`, PgHook uses a generated temporary slot (`pghook_<guid>`).

---

## Failure & retry behavior

- Events are read continuously from the replication stream.  
- Webhook deliveries use an HTTP client configured with your timeout/keep-alive settings.  
- Non-success responses (>=400) are logged and retried three times, after 2 seconds, 4 seconds and 8 seconds.
- If the HTTP request failes after three retries, the replication stops, database connection is closed.
  After 10 seconds the replication is restarted, and the process starts from the beginning. 

---

## Example: docker-compose

```yaml
services:
  pghook:
    image: pghook/pghook
    container_name: pghook
    environment:
      PGH_POSTGRES_CONN: "Host=db;Username=postgres;Password=secret;Database=postgres;ApplicationName=PgHook"
      PGH_PUBLICATION_NAMES: "mypub"
      PGH_WEBHOOK_URL: "http://test-api:5000/webhooks"
      # PGH_USE_PERMANENT_SLOT: "true"
      # PGH_REPLICATION_SLOT: "myslot"
      # PGH_BATCH_SIZE: "100"
      # PGH_JSON_COMPACT: "true"
    depends_on:
      - db
      - test-api

  test-api:
    image: pghook/test-api
    container_name: test-api
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_URLS=http://0.0.0.0:5000
      - Logging__LogLevel__Default=Information
      - Logging__LogLevel__Microsoft.AspNetCore=Warning

  db:
    image: postgres:17-alpine
    container_name: db
    ports:
      - "5432:5432"
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: secret
      POSTGRES_DB: postgres
    volumes:
      - postgres_data:/var/lib/postgresql/data
    command: [
      "postgres",
      "-c", "wal_level=logical",
      "-c", "max_wal_senders=10",
      "-c", "max_replication_slots=10"
    ]
    shm_size: 128mb
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
    name: pghook_postgres_data 
```

---

## Testing the example docker-compose.yml

### Follow `test-api` logs

Open a terminal and run:

```bash
docker compose up -d              # start everything in background
docker compose logs -f test-api   # follow logs for the test-api container
```

This will stream HTTP requests received by the test API in real time.

### Quick test with PostgreSQL

In __another__ terminal, execute the following commands (to create table and publication in the `db` container):

```
# Create a test table
docker exec -it db psql -U postgres -c "CREATE TABLE test_table (id SERIAL PRIMARY KEY, name TEXT);"

# Create a publication for the table
docker exec -it db psql -U postgres -c "CREATE PUBLICATION mypub FOR TABLE test_table;"

# Insert a few rows (add more inserts as needed)
docker exec -it db psql -U postgres -c "INSERT INTO test_table (name) VALUES ('Alice');"
docker exec -it db psql -U postgres -c "INSERT INTO test_table (name) VALUES ('Bob');"
docker exec -it db psql -U postgres -c "INSERT INTO test_table (name) VALUES ('Charlie');"
```

PgHook should pick up these changes and POST them to the `test-api` webhook, which you can see in the logs window.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
