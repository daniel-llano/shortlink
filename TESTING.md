# Testing ShortLink

## Unit and integration

```bash
./scripts/test.sh
```

```powershell
.\scripts\test.ps1
```

- `tests/Rle.Tests`: encode and decode round trips, digit rejection, malformed input.
- `tests/ShortLink.Api.Tests`: the API through `WebApplicationFactory` on a temporary SQLite file. Covers creation, aliases, reserved words, bad targets (non-http schemes, plain http, links back to the shortener), expiry, owner-key deletes, QR output, the optional API key, and 21 concurrent redirects that must all be counted.

CI also runs `dotnet ef migrations has-pending-model-changes` against the PostgreSQL model.

## End to end

Needs Docker and the Playwright CLI (`npm install -g playwright`).

```bash
./scripts/e2e.sh
```

It brings up PostgreSQL, Redis, and the app from `infra/docker-compose.yml`, then shortens a link in the UI, follows it, checks the click count, deletes it, and exercises alias conflicts and validation messages.
