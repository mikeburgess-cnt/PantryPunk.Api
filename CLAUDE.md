# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Specification

The full API specification is in `docs/spec-api.md`. Always consult it for detailed endpoint behaviour, request/response shapes, and business rules before implementing or modifying features.

## Build & Run Commands

```bash
# Build
dotnet build PantryPunk.Api/PantryPunk.Api.csproj

# Run (HTTPS: https://localhost:7172, HTTP: localhost:5280)
dotnet run --project PantryPunk.Api/PantryPunk.Api.csproj

# Run with hot reload
dotnet watch --project PantryPunk.Api/PantryPunk.Api.csproj
```

Tests live in `PantryPunk.Api.Tests/`. Run with `dotnet test` from the solution root.

## Architecture

- **Solution:** `PantryPunk.Api.slnx` — single-project ASP.NET Core Web API targeting **net10.0** (C# 13)
- **Hosting model:** Minimal hosting (`Program.cs`) with controller-based routing
- **Pattern:** Controllers → Services → Repositories (3-layer)
- **Database:** Azure Cosmos DB (NoSQL, SQL API) — items are embedded within the `ShoppingListDocument`, not stored separately
- **File storage:** Azure Blob Storage (`photos` container, public read)
- **Auth:** Auth0 JWT for registered users + `X-Share-Code` header for guest access (validated by `ShareCodeAuthMiddleware`)
- **Runtime config & feature flags:** Cosmos `AppConfig` container (read per request by `AppConfigMiddleware`, 30s in-memory cache, exposed via `IConfiguration` overlay) + `Microsoft.FeatureManagement`
- **AI:** Claude API (Sonnet) for image recognition
- **Secrets:** Azure Key Vault loaded into `IConfiguration` at startup via managed identity
- **Monitoring:** Application Insights

## Project Layout

All source lives under `PantryPunk.Api/`:

- `Program.cs` — app builder, DI registration, middleware pipeline
- `Controllers/` — `UserController`, `ListController`, `ImageController`, `ShareController`, `HouseholdController`, `FeatureController`, `WebhookController`
- `Services/` — business logic layer (`UserService`, `ListService`, `ShareService`, `HouseholdService`, `ImageRecognitionService`, `BlobStorageService`, `FeatureFlagService`)
- `Repositories/` — Cosmos DB data access (`UserRepository`, `ListRepository`, `ShareRepository`, `AppConfigRepository`)
- `Models/Documents/` — Cosmos DB document classes (`UserDocument`, `ShoppingListDocument`, `ShoppingItemDocument`, `ShareCodeDocument`, `AppConfigDocument`)
- `Models/Requests/` — inbound DTOs
- `Models/Responses/` — outbound DTOs
- `Middleware/` — `ShareCodeAuthMiddleware` (guest auth), `AppConfigMiddleware` (per-request Cosmos config overlay), `RequestLoggingMiddleware`
- `Extensions/` — `ClaimsPrincipalExtensions` (Auth0 sub extraction), `ServiceCollectionExtensions` (DI helpers)
- `Infrastructure/` — `CosmosDbContext`, `AmbientConfigurationProvider` (AsyncLocal-backed `IConfigurationSource` for the per-request overlay), `KeyVaultSetup` (loads Key Vault secrets at startup)

## Key Design Decisions

- **Embedded items:** Shopping list items are an array inside `ShoppingListDocument`, not a separate container. This keeps reads as single point reads. Designed for household scale (< 100 items).
- **User identity:** Auth0 `sub` claim (e.g. `auth0|abc123`) is the canonical user ID everywhere. Extract via `User.FindFirst(ClaimTypes.NameIdentifier)?.Value`.
- **addedBy resolution:** Never accepted from client. For JWT users, read from `UserDocument.DisplayName`. For guests, read from `RecipientName` claim injected by `ShareCodeAuthMiddleware`.
- **Subscriber checks:** `isSubscriber` is NOT in the JWT — it lives in Cosmos DB `UserDocument`. Service layer loads the document and checks, not a policy claim.
- **Share codes:** 6-char uppercase alphanumeric, partition key is `/code`. Soft-deleted via `revokedAt` (never hard-delete). Expire after 24 hours if unconfirmed; valid indefinitely once confirmed. Recipient name is supplied by the guest when they confirm the code (not by the subscriber at generation time); first-confirm wins for the stored name. Only subscribers can generate or list share codes. Revoke (`DELETE /api/share/:shareId`) accepts either an authenticated subscriber (any code they own) or a share-code guest revoking **their own** code — enforced in `ShareService` via `UserService.RequireSubscriberAsync` and a `ShareId` claim the middleware surfaces. The `RegisteredUser` policy rejects any principal carrying the `RecipientName` claim (the share-code middleware's marker), so share-code guests cannot reach JWT-only endpoints — while leaving the dev `X-Api-Key` / `DevAuthMiddleware` identity untouched. `POST /api/share/confirm-code` returns the full `ShareCodeResponse` (including `shareId`) on 200 so the guest can call `DELETE /api/share/:shareId` to self-revoke; errors use the standard `{ error, traceId }` shape.
- **Photo upload + recognition:** Single endpoint (`POST /api/shopping-list/items/photo`) uploads blob, calls Claude, creates item — all in one request. Low confidence items are still created (not 422).

## Cosmos DB Containers

| Container | Partition Key | Document |
|---|---|---|
| `Users` | `/userId` | `UserDocument` |
| `ShoppingLists` | `/listId` | `ShoppingListDocument` (with embedded items) |
| `ShareCodes` | `/code` | `ShareCodeDocument` |
| `AppConfig` | `/id` | `AppConfigDocument` (single doc, id `app-config`, flat `settings` dict) |

Database name: `PantryPunkDb`

## Authentication Modes

| Mode | Header | Who |
|---|---|---|
| Auth0 JWT | `Authorization: Bearer <token>` | Registered users |
| Share Code | `X-Share-Code: <code>` | Non-subscriber guests |
| None | — | `POST /api/share/confirm-code` and `POST /api/webhooks/revenuecat` only |

If both headers present, JWT takes precedence. `DELETE /api/share/:shareId` and `GET /api/household/members` accept either mode (for the DELETE, guests may only revoke their own code; for the household read, the share-code middleware surfaces the owner's userId via the `NameIdentifier` claim so the same handler serves both auth paths). All other `/api/share/*` endpoints are JWT-only and require `isSubscriber == true`.

## Error Response Shape

All errors use `{ "error": "message", "traceId": "..." }`. See spec for status code usage — notably `410 Gone` for expired/revoked share codes and `422` for AI failures (but NOT for low confidence).

## Local Development

- Use **Cosmos DB Emulator** and **Azurite** for local storage
- Seed an `AppConfig` container with the doc from `infra/seed/app-config.json` to exercise the overlay locally; without it, the middleware falls back and `PantryPunk:*` / `FeatureManagement:*` values come from `appsettings.Development.json`
- Store local secrets via `dotnet user-secrets` (Key Vault is a no-op locally when `KeyVault:Uri` is unset)
- Claude model config key: `PantryPunk:Claude:Model` (default `claude-sonnet-4-6`)
