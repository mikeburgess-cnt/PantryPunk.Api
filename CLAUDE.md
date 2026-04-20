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

No test project exists yet. When one is added, it should use `dotnet test` from the solution root.

## Architecture

- **Solution:** `PantryPunk.Api.slnx` — single-project ASP.NET Core Web API targeting **net10.0** (C# 13)
- **Hosting model:** Minimal hosting (`Program.cs`) with controller-based routing
- **Pattern:** Controllers → Services → Repositories (3-layer)
- **Database:** Azure Cosmos DB (NoSQL, SQL API) — items are embedded within the `ShoppingListDocument`, not stored separately
- **File storage:** Azure Blob Storage (`photos` container, public read)
- **Auth:** Auth0 JWT for registered users + `X-Share-Code` header for guest access (validated by `ShareCodeAuthMiddleware`)
- **Feature flags:** Azure App Configuration + `Microsoft.FeatureManagement`
- **AI:** Claude API (Sonnet) for image recognition and voice item extraction; Azure AI Speech for audio transcription
- **Secrets:** Azure Key Vault via managed identity
- **Monitoring:** Application Insights

## Project Layout

All source lives under `PantryPunk.Api/`:

- `Program.cs` — app builder, DI registration, middleware pipeline
- `Controllers/` — `UserController`, `ListController`, `ImageController`, `VoiceController`, `ShareController`, `FeatureController`, `WebhookController`
- `Services/` — business logic layer (`UserService`, `ListService`, `ShareService`, `ImageRecognitionService`, `VoiceRecognitionService`, `BlobStorageService`, `FeatureFlagService`)
- `Repositories/` — Cosmos DB data access (`UserRepository`, `ListRepository`, `ShareRepository`)
- `Models/Documents/` — Cosmos DB document classes (`UserDocument`, `ShoppingListDocument`, `ShoppingItemDocument`, `ShareCodeDocument`)
- `Models/Requests/` — inbound DTOs
- `Models/Responses/` — outbound DTOs
- `Middleware/` — `ShareCodeAuthMiddleware` (guest auth), `RequestLoggingMiddleware`
- `Extensions/` — `ClaimsPrincipalExtensions` (Auth0 sub extraction), `ServiceCollectionExtensions` (DI helpers)
- `Infrastructure/` — `CosmosDbContext`, `AppConfigurationSetup`, `KeyVaultConfiguration`

## Key Design Decisions

- **Embedded items:** Shopping list items are an array inside `ShoppingListDocument`, not a separate container. This keeps reads as single point reads. Designed for household scale (< 100 items).
- **User identity:** Auth0 `sub` claim (e.g. `auth0|abc123`) is the canonical user ID everywhere. Extract via `User.FindFirst(ClaimTypes.NameIdentifier)?.Value`.
- **addedBy resolution:** Never accepted from client. For JWT users, read from `UserDocument.DisplayName`. For guests, read from `RecipientName` claim injected by `ShareCodeAuthMiddleware`.
- **Subscriber checks:** `isSubscriber` is NOT in the JWT — it lives in Cosmos DB `UserDocument`. Service layer loads the document and checks, not a policy claim.
- **Share codes:** 6-char uppercase alphanumeric, partition key is `/code`. Soft-deleted via `revokedAt` (never hard-delete). Expire after 24 hours if unconfirmed; valid indefinitely once confirmed. Recipient name is supplied by the guest when they confirm the code (not by the subscriber at generation time); first-confirm wins for the stored name. Only subscribers can generate or list share codes. Revoke (`DELETE /api/share/:shareId`) accepts either an authenticated subscriber (any code they own) or a share-code guest revoking **their own** code — enforced in `ShareService` via `UserService.RequireSubscriberAsync` and a `ShareId` claim the middleware surfaces. The `RegisteredUser` policy rejects any principal carrying the `RecipientName` claim (the share-code middleware's marker), so share-code guests cannot reach JWT-only endpoints — while leaving the dev `X-Api-Key` / `DevAuthMiddleware` identity untouched.
- **Photo upload + recognition:** Single endpoint (`POST /api/shopping-list/items/photo`) uploads blob, calls Claude, creates item — all in one request. Low confidence items are still created (not 422).
- **Voice flow:** Audio → Azure AI Speech (transcription) → Claude (item extraction) → items created. Single endpoint (`POST /api/shopping-list/items/voice`).

## Cosmos DB Containers

| Container | Partition Key | Document |
|---|---|---|
| `Users` | `/userId` | `UserDocument` |
| `ShoppingLists` | `/listId` | `ShoppingListDocument` (with embedded items) |
| `ShareCodes` | `/code` | `ShareCodeDocument` |

Database name: `PantryPunkDb`

## Authentication Modes

| Mode | Header | Who |
|---|---|---|
| Auth0 JWT | `Authorization: Bearer <token>` | Registered users |
| Share Code | `X-Share-Code: <code>` | Non-subscriber guests |
| None | — | `POST /api/share/confirm-code` and `POST /api/webhooks/revenuecat` only |

If both headers present, JWT takes precedence. `DELETE /api/share/:shareId` uniquely accepts either mode (guests may only revoke their own code); all other `/api/share/*` endpoints are JWT-only and require `isSubscriber == true`.

## Error Response Shape

All errors use `{ "error": "message", "traceId": "..." }`. See spec for status code usage — notably `410 Gone` for expired/revoked share codes and `422` for AI failures (but NOT for low confidence).

## Local Development

- Use **Cosmos DB Emulator** and **Azurite** for local storage
- Override feature flags and `PantryPunk:*` config in `appsettings.Development.json` (see spec for shape)
- Store local secrets via `dotnet user-secrets` or git-ignored `appsettings.Development.json`
- Claude model config key: `PantryPunk:Claude:Model` (default `claude-sonnet-4-6`)
