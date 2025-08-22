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

> Want a stable replication position across restarts? Add a permanent slot:  
> `-e PGH_USE_PERMANENT_SLOT=true \`  
> `-e PGH_REPLICATION_SLOT=myslot`  
> 
> ⚠️ **Important:** Permanent replication slots persist across crashes and know nothing about the state of their consumer(s). 
> They will prevent removal of required resources when there is no connection using them. 
> This consumes storage because neither required WAL nor required rows from the system catalogs can be removed by VACUUM as long as they are required by a replication slot. 
> **In extreme cases, this could cause the database to shut down to prevent transaction ID wraparound.**  
> 
> **So, if a slot is no longer required, it should be dropped.** To drop a replication slot, use:  
> `SELECT * FROM pg_drop_replication_slot('my_slot');`

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

## Testing with a local executable file

```bash
docker run --rm \
  -e PGH_POSTGRES_CONN="Host=mydbserver;Username=replicator;Password=secret;Database=mydb;ApplicationName=PgHook" \
  -e PGH_PUBLICATION_NAMES="mypub" \
  -e PGH_WEBHOOK_URL="file:///bin/cat" \
  pghook/pghook
```

### Webhook metadata

If `PGH_USE_STANDARD_WEBHOOKS` is `false`, which is the default, each request includes:
- `X-Timestamp`: Unix timestamp of when the payload was signed
- `X-Hub-Signature-256`: HMAC-SHA256 of the request body using your secret (GitHub style, optional, only sent if `options.WebhookSecret` is set)
  See: [Validating Webhook Deliveries](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)

If `PGH_USE_STANDARD_WEBHOOKS` is `true` then each request includes standard headers:
- `webhook-id`: Id of the message in format FirstDedupKey_LastDedupKey, (eg. `2485645760_2485645760`). 
  Note that this is not fully standard compliant. It can only be used for idempotency check if the `BatchSize` is 1.
  Otherwise, deduplication keys from the individual messages should be used.
- `webhook-timestamp`: Integer unix timestamp (seconds since epoch).
- `webhook-signature`: The signature of the webhook. See: [Standard Webhooks](https://www.standardwebhooks.com/).

In both cases, the request includes:
- `User-Agent`: string in this format: `PgHook/ReplicationSlotName`;

---

## Configuration

All configuration is via environment variables. Required ones first; everything else is optional.

**Environment Variables**

- **`PGH_POSTGRES_CONN`** *(string, required)* - PostgreSQL connection string (Npgsql format).  
- **`PGH_PUBLICATION_NAMES`** *(string, required)* - Comma-separated publication name(s) to subscribe to.  
- **`PGH_WEBHOOK_URL`** *(string, required)* – Webhook endpoint that receives change batches via HTTP POST. 
  If a `file://` scheme is provided (`file:///local/file/path`), PgHook will execute the specified file instead of making an HTTP request. 
  The headers and the JSON payload will be written to the process’s **standard input**, and PgHook will capture **standard output** and **standard error**, logging both to the console.  
- **`PGH_EXEC_FILE_ARGS`** *(string, default: empty)* – Additional command-line arguments to pass when executing the file specified in `PGH_WEBHOOK_URL`.  
- **`PGH_REPLICATION_SLOT`** *(string, default: auto-generated unless using permanent slot)* - Replication slot name. Required if `PGH_USE_PERMANENT_SLOT=true`.  
- **`PGH_USE_PERMANENT_SLOT`** *(bool, default: `false`)* - Use a permanent logical replication slot instead of a temporary one.  
- **`PGH_BATCH_SIZE`** *(int, default: `100`)* - Max number of change events per POST.  
- **`PGH_JSON_COMPACT`** *(bool, default: `false`)* - Emit compact JSON (minified).  
- **`PGH_USE_STANDARD_WEBHOOKS`** *(bool, default: `false`)* - If true, uses standard webhooks headers and signature scheme (see **Webhook metadata**).  
- **`PGH_WEBHOOK_SECRET`** *(string, default: empty)* - If set, requests are signed (see **Webhook metadata**).  
- **`PGH_WEBHOOK_SECRET_1`** *(string, default: empty)* - Only used if `PGH_USE_STANDARD_WEBHOOKS` is `true`. If set, additional signatures are added (to allow key rotation).
  More keys can be added by adding more variables with increasing number suffix, eg: `PGH_WEBHOOK_SECRET_2`, `PGH_WEBHOOK_SECRET_3`...  
- **`PGH_WEBHOOK_TIMEOUT_SEC`** *(int, default: `30`)* - Overall HTTP request timeout.  
- **`PGH_WEBHOOK_CONNECT_TIMEOUT_SEC`** *(int, default: `10`)* - Connect timeout for the HTTP client.  
- **`PGH_WEBHOOK_KEEPALIVE_DELAY_SEC`** *(int, default: `60`)* - TCP keep-alive probe delay.  
- **`PGH_WEBHOOK_KEEPALIVE_TIMEOUT_SEC`** *(int, default: `10`)* - TCP keep-alive probe timeout.  
- **`PGH_WEBHOOK_POOLED_CONNECTION_LIFETIME_SEC`** *(int, default: `600`)* - Max lifetime for pooled HTTP connections.  
- **`PGH_WEBHOOK_POOLED_CONNECTION_IDLE_TIMEOUT_SEC`** *(int, default: `120`)* - Idle timeout for pooled HTTP connections.  

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
      PGH_PUBLICATION_NAMES: mypub
      PGH_WEBHOOK_URL: http://test-api:5000/webhooks
      PGH_WEBHOOK_SECRET: test-secret
      # PGH_USE_STANDARD_WEBHOOKS: true
      # PGH_USE_PERMANENT_SLOT: true
      # PGH_REPLICATION_SLOT: myslot
      # PGH_BATCH_SIZE: 100
      # PGH_JSON_COMPACT: true
    depends_on:
      - db
      - test-api

  test-api:
    image: pghook/test-api
    container_name: test-api
    ports:
      - "5000:5000"
    environment:
      PGH_WEBHOOK_SECRET: test-secret
      ASPNETCORE_URLS: http://0.0.0.0:5000
      Logging__LogLevel__Default: Information
      Logging__LogLevel__Microsoft.AspNetCore: Warning

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
docker exec -it db psql -U postgres -c "INSERT INTO test_table (name) VALUES ('Charlie'), ('Angel');"
```

PgHook should pick up these changes and POST them to the `test-api` webhook, which you can see in the logs window.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
