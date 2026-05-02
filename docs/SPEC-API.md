# PantryPunk — Backend API Specification

## Table of Contents

1. [Overview](#overview)
2. [Tech Stack](#tech-stack)
3. [Architecture](#architecture)
4. [Azure Infrastructure](#azure-infrastructure)
5. [Project Structure](#project-structure)
6. [Authentication & Authorisation](#authentication--authorisation)
7. [Cosmos DB Data Model](#cosmos-db-data-model)
8. [API Endpoints](#api-endpoints)
   - [User Profile](#user-profile)
   - [Shopping List](#shopping-list)
   - [AI — Voice Recognition](#ai--voice-recognition)
   - [Sharing](#sharing)
   - [Feature Flags](#feature-flags-endpoint)
   - [Subscriptions (RevenueCat Webhook)](#subscriptions-revenuecat-webhook)
9. [Claude API Integration](#claude-api-integration)
10. [RevenueCat Webhook Handling](#revenuecat-webhook-handling)
11. [Runtime Configuration (AppConfig)](#runtime-configuration-appconfig)
12. [File Storage](#file-storage)
13. [Error Handling](#error-handling)
14. [Logging & Monitoring](#logging--monitoring)
15. [Security](#security)
16. [Development & Deployment](#development--deployment)

---

## Overview

The backend is an ASP.NET Core Web API that serves the PantryPunk Flutter app. It is the single source of truth for all shopping list data. It handles:

- User profile management (display name, subscription status)
- Shopping list CRUD operations
- Shared list access via share codes
- AI-powered image recognition (via Claude API)
- AI-powered voice transcription (via Claude API)
- Subscription status management (via RevenueCat webhooks)
- Photo storage (via Azure Blob Storage)

Authentication is handled by two mechanisms: Auth0 JWT for registered users, and share codes for non-subscriber guests. The API verifies Auth0 JWTs on registered-user requests — it never issues its own tokens or stores passwords. Share codes are validated against Cosmos DB by middleware before list endpoints are reached.

---

## Tech Stack

| Concern | Choice |
|---|---|
| Framework | ASP.NET Core 10 Web API |
| Language | C# 13 |
| Runtime | .NET 10 |
| Database | Azure Cosmos DB (NoSQL, SQL API) |
| File storage | Azure Blob Storage |
| Authentication | Auth0 JWT verification (`Microsoft.AspNetCore.Authentication.JwtBearer`) |
| Configuration & feature flags | Cosmos `AppConfig` container (per-request overlay) + `Microsoft.FeatureManagement.AspNetCore` |
| Claude API client | Anthropic .NET SDK or raw `HttpClient` |
| Speech-to-text | Azure AI Speech (Speech-to-Text) — audio transcription for voice recognition |
| Hosting | Azure App Service (Linux, B2 or higher) |
| CI/CD | Azure DevOps Pipelines |
| Logging | Azure Application Insights |
| Secrets management | Azure Key Vault |
| HTTP client | `IHttpClientFactory` with typed clients |

---

## Architecture

```
Flutter App
    │
    │ HTTPS
    ▼
┌───────────────────────────────────────┐
│         Azure App Service             │
│                                       │
│   ASP.NET Core 10 Web API             │
│                                       │
│  ┌─────────────────────────────────┐  │
│  │         Middleware Pipeline     │  │
│  │  HTTPS redirect → CORS →        │  │
│  │  Auth0 JWT → Rate limiting →    │  │
│  │  Request logging                │  │
│  └──────────────┬──────────────────┘  │
│                 │                     │
│  ┌──────────────▼──────────────────┐  │
│  │           Controllers           │  │
│  │  UserController                 │  │
│  │  ListController                 │  │
│  │  ImageController                │  │
│  │  VoiceController                │  │
│  │  ShareController                │  │
│  │  WebhookController              │  │
│  └──────────────┬──────────────────┘  │
│                 │                     │
│  ┌──────────────▼──────────────────┐  │
│  │            Services             │  │
│  │  UserService                    │  │
│  │  ListService                    │  │
│  │  ShareService                   │  │
│  │  ImageRecognitionService        │  │
│  │  VoiceRecognitionService        │  │
│  │  BlobStorageService             │  │
│  └──────────────┬──────────────────┘  │
│                 │                     │
│  ┌──────────────▼──────────────────┐  │
│  │          Repositories           │  │
│  │  UserRepository                 │  │
│  │  ListRepository                 │  │
│  │  ShareRepository                │  │
│  └──────────────┬──────────────────┘  │
└─────────────────┼─────────────────────┘
                  │
    ┌─────────────┼──────────────┐
    │             │              │
    ▼             ▼              ▼
Cosmos DB    Azure Blob     External APIs
             Storage        (Claude,
                            RevenueCat,
                            Auth0)
```

---

## Azure Infrastructure

### Resources

| Resource | Type | Purpose |
|---|---|---|
| `pantrypunk-api` | Azure App Service | Hosts the ASP.NET Core API |
| `pantrypunk-db` | Azure Cosmos DB account | Primary database |
| `pantrypunkstorage` | Azure Storage Account | Photo blob storage |
| `pantrypunk-insights` | Application Insights | Logging and monitoring |
| `pantrypunk-vault` | Azure Key Vault | Secrets (connection strings, API keys) |
| `pantrypunk-speech` | Azure AI Speech | Speech-to-text transcription for Talk It feature |
| `pantrypunk-plan` | App Service Plan | Compute for the App Service |

### App Service Configuration

- **Runtime:** .NET 10 on Linux
- **Plan:** B2 minimum (supports always-on, required to avoid cold starts)
- **Always On:** Enabled
- **HTTPS Only:** Enabled
- **Environment variables:** All secrets loaded from Azure Key Vault via managed identity — never stored in App Settings directly

### Managed Identity

The App Service uses a **system-assigned managed identity** to access:
- Azure Key Vault (secrets)
- Azure Blob Storage (photo upload/read)
- Azure Cosmos DB (data plane — also serves the `AppConfig` container)
- Azure AI Speech (speech-to-text transcription)

No connection string credentials need to be stored in environment variables.

---

## Project Structure

```
PantryPunkApi/
├── PantryPunkApi.csproj
├── Program.cs                          # App bootstrap, DI registration
├── appsettings.json                    # Non-secret configuration
├── appsettings.Development.json        # Local dev overrides
│
├── Controllers/
│   ├── UserController.cs
│   ├── ListController.cs
│   ├── ImageController.cs
│   ├── VoiceController.cs
│   ├── ShareController.cs
│   ├── FeatureController.cs               # GET /api/features
│   └── WebhookController.cs
│
├── Services/
│   ├── UserService.cs
│   ├── ListService.cs
│   ├── ShareService.cs
│   ├── ImageRecognitionService.cs
│   ├── VoiceRecognitionService.cs
│   ├── FeatureFlagService.cs              # Wraps IFeatureManager
│   └── BlobStorageService.cs
│
├── Repositories/
│   ├── UserRepository.cs
│   ├── ListRepository.cs
│   └── ShareRepository.cs
│
├── Models/
│   ├── Documents/                      # Cosmos DB documents
│   │   ├── UserDocument.cs
│   │   ├── ShoppingListDocument.cs
│   │   └── ShareCodeDocument.cs
│   ├── Requests/                       # Inbound DTOs
│   │   ├── AddItemRequest.cs           # description, quantity, notes only — addedBy resolved server-side
│   │   ├── UpdateItemRequest.cs        # description, quantity, notes
│   │   ├── GenerateShareCodeRequest.cs
│   │   ├── ConfirmShareCodeRequest.cs
│   │   └── UpdateProfileRequest.cs
│   └── Responses/                      # Outbound DTOs
│       ├── UserProfileResponse.cs
│       ├── ShoppingListResponse.cs
│       ├── ShoppingItemResponse.cs
│       └── ShareCodeResponse.cs
│
├── Middleware/
│   ├── ShareCodeAuthMiddleware.cs      # Validates X-Share-Code header
│   └── RequestLoggingMiddleware.cs
│
├── Extensions/
│   ├── ClaimsPrincipalExtensions.cs    # Helper to extract Auth0 sub claim
│   └── ServiceCollectionExtensions.cs # DI registration helpers
│
└── Infrastructure/
    ├── CosmosDbContext.cs
    ├── AmbientConfigurationProvider.cs    # AsyncLocal-backed IConfigurationSource for per-request overlay
    └── KeyVaultSetup.cs                   # Loads Key Vault secrets into IConfiguration at startup
```

---

## Authentication & Authorisation

### Authentication Modes

The API supports two authentication modes. Every request must include exactly one of the following, except for the two public endpoints noted in the table below:

| Mode | Header | Who uses it | Applies to |
|---|---|---|---|
| Auth0 JWT | `Authorization: Bearer <token>` | Registered users (free and subscriber) | All endpoints requiring user identity |
| Share Code | `X-Share-Code: <code>` | Non-subscriber guests | List read/write endpoints only |
| None | — | Anonymous | `POST /api/share/confirm-code` and `POST /api/webhooks/revenuecat` only |

If both `Authorization` and `X-Share-Code` headers are present on the same request, the `Authorization` Bearer token takes precedence and the share code is ignored.

### Auth0 JWT Verification

Registered users authenticate with an Auth0 JWT access token obtained via the `auth0_flutter` SDK:

```
Authorization: Bearer <auth0_access_token>
```

Configure JWT Bearer authentication in `Program.cs`:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });
```

The Auth0 `sub` claim (e.g. `auth0|abc123` or `google-oauth2|abc123`) is the canonical user identifier throughout the system. Extract it via:

```csharp
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

### Share Code Authentication

Non-subscriber guests do not have an Auth0 token. They authenticate via the `X-Share-Code` header:

```
X-Share-Code: 6Y812C
```

`ShareCodeAuthMiddleware` intercepts requests that include this header (without an `Authorization` header), validates the code against the `ShareCodes` Cosmos DB container, and if valid, injects a synthetic claims principal with:
- `NameIdentifier` claim = `ownerUserId` (the subscriber's user ID — used to look up the list)
- A custom `RecipientName` claim = `ShareCodeDocument.RecipientName`, i.e. the name the guest supplied when they confirmed the code (used as `addedBy` on items the guest adds)

The list controller and service layer read these claims from the principal — no further container lookups are needed for authentication or `addedBy` resolution.

If both headers are present, the `Authorization` Bearer token takes precedence.

### Authorisation Policy

Define one policy for authenticated requests. Subscriber-only access is **not** enforced via a JWT claim policy because `isSubscriber` is not present in the Auth0 JWT — it lives in Cosmos DB. Subscriber checks are performed in the service layer by loading the `UserDocument` from Cosmos DB.

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RegisteredUser", policy =>
        policy.RequireAuthenticatedUser());
});
```

Endpoints that require subscriber status (e.g. `POST /api/share/generate-code`) call `UserService.RequireSubscriberAsync(userId)` which loads the `UserDocument` and throws a `ForbiddenException` if `IsSubscriber == false`.

### Endpoint Security Summary

| Endpoint | Auth required | Notes |
|---|---|---|
| `POST /api/users/profile` | Auth0 JWT | First sign-in profile creation |
| `GET /api/users/profile` | Auth0 JWT | |
| `GET /api/shopping-list` | JWT or Share Code | |
| `POST /api/shopping-list/items` | JWT or Share Code | |
| `PUT /api/shopping-list/items/:id` | JWT or Share Code | |
| `DELETE /api/shopping-list/items/:id` | JWT or Share Code | |
| `POST /api/shopping-list/complete` | JWT or Share Code | Mark active list completed, create new active list |
| `POST /api/shopping-list/items/photo` | JWT or Share Code | Upload photo, recognise item, add to list |
| `POST /api/shopping-list/items/voice` | JWT or Share Code | Transcribe audio, extract items, add to list |
| `POST /api/share/generate-code` | JWT + isSubscriber | |
| `POST /api/share/confirm-code` | None | Public endpoint |
| `GET /api/share` | JWT + isSubscriber | Replaces polling status endpoint; share-code guests are rejected |
| `DELETE /api/share/:shareId` | (JWT + isSubscriber) or Share Code (self only) | Guests may only revoke the code they authenticated with |
| `POST /api/webhooks/revenuecat` | RevenueCat signature | Not Auth0 |

---

## Cosmos DB Data Model

### Account & Containers

- **Database name:** `PantryPunkDb`
- **Consistency level:** Session (default — sufficient for this app)

### Containers

#### `Users`

Stores registered user profiles.

- **Partition key:** `/userId`

```json
{
  "id": "auth0|abc123",
  "userId": "auth0|abc123",
  "displayName": "Mike",
  "email": "mike@example.com",
  "isSubscriber": false,
  "subscribedAt": null,
  "subscriptionExpiresAt": null,
  "createdAt": "2026-04-11T08:00:00Z",
  "updatedAt": "2026-04-11T08:00:00Z"
}
```

---

#### `ShoppingLists`

One active list per household, plus one document per historical completion. Items are embedded as an array within the list document — no separate items container needed at this scale.

- **Partition key:** `/listId`
- `listId` is a **stable per-household identifier**, assigned once when the user's profile is first created and never rotated. Multiple documents may share the same `listId`: exactly one with `status = "active"`, any number with `status = "completed"`. This keeps all of a household's history in a single partition and, critically, keeps share codes (which bind to `listId`) valid across completions.
- `id` is per-document and unique within the partition.

```json
{
  "id": "list-uuid",
  "listId": "list-uuid",
  "ownerUserId": "auth0|abc123",
  "items": [
    {
      "id": "item-uuid-1",
      "description": "Coles Full Cream Milk (3L)",
      "quantity": 2,
      "addedBy": "Mike",
      "notes": null,
      "photoUrl": "https://pantrypunkstorage.blob.core.windows.net/photos/item-uuid-1.jpg",
      "confidence": "high",
      "createdAt": "2026-04-11T08:00:00Z",
      "updatedAt": "2026-04-11T08:00:00Z"
    }
  ],
  "createdAt": "2026-04-11T08:00:00Z",
  "updatedAt": "2026-04-11T08:00:00Z"
}
```

> **Embedded items rationale:** For a single household shopping list, the number of items will be small (typically < 100). Embedding items in the list document avoids the complexity of cross-partition queries and keeps all list reads as a single point read — the most efficient operation in Cosmos DB.

---

#### `ShareCodes`

Stores share codes generated by subscribers for non-subscriber guests.

- **Partition key:** `/code`

```json
{
  "id": "sharecode-uuid",
  "code": "6Y812C",
  "listId": "list-uuid",
  "ownerUserId": "auth0|abc123",
  "recipientName": "Natalie",
  "confirmed": false,
  "confirmedAt": null,
  "revokedAt": null,
  "expiresAt": "2026-04-12T08:00:00Z",
  "createdAt": "2026-04-11T08:00:00Z"
}
```

**Share code generation:**
- 6-character alphanumeric code, uppercase (e.g. `6Y812C`)
- Generated using `Guid.NewGuid()` hash or `RandomNumberGenerator` — must be collision-checked against existing active codes before saving
- Expires 24 hours after generation if not confirmed
- Once confirmed, the code remains valid indefinitely until explicitly revoked

---

### C# Document Models

```csharp
// Users container
public class UserDocument
{
    [JsonProperty("id")]
    public string Id { get; set; }           // Same as UserId

    [JsonProperty("userId")]
    public string UserId { get; set; }       // Auth0 sub claim

    [JsonProperty("displayName")]
    public string DisplayName { get; set; }

    [JsonProperty("email")]
    public string? Email { get; set; }

    [JsonProperty("isSubscriber")]
    public bool IsSubscriber { get; set; }

    [JsonProperty("subscribedAt")]
    public DateTime? SubscribedAt { get; set; }

    [JsonProperty("subscriptionExpiresAt")]
    public DateTime? SubscriptionExpiresAt { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// ShoppingLists container
public class ShoppingListDocument
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("listId")]
    public string ListId { get; set; }

    [JsonProperty("ownerUserId")]
    public string OwnerUserId { get; set; }

    [JsonProperty("items")]
    public List<ShoppingItemDocument> Items { get; set; } = new();

    // "active" or "completed". Exactly one active list per user. Legacy docs without
    // this field are treated as active by the active-list query.
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    // Set when the list is marked completed; null while active.
    [JsonProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

public class ShoppingItemDocument
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("quantity")]
    public int? Quantity { get; set; }

    [JsonProperty("addedBy")]
    public string AddedBy { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("photoUrl")]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Claude's self-assessed confidence in the image recognition result.
    /// "high" | "medium" | "low". Null for items added manually or by voice.
    /// </summary>
    [JsonProperty("confidence")]
    public string? Confidence { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Set true at list completion for items the shopper actually bought.
    /// Items left unbought (carried over to the next list) and items on the
    /// active list both read false. Persisted on completed list documents
    /// for analytics (purchase frequency, never-bought items, fulfilment).
    /// </summary>
    [JsonProperty("isPurchased")]
    public bool IsPurchased { get; set; }
}

// ShareCodes container
public class ShareCodeDocument
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("listId")]
    public string ListId { get; set; }

    [JsonProperty("ownerUserId")]
    public string OwnerUserId { get; set; }

    [JsonProperty("recipientName")]
    public string RecipientName { get; set; }

    [JsonProperty("confirmed")]
    public bool Confirmed { get; set; }

    [JsonProperty("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    [JsonProperty("revokedAt")]
    public DateTime? RevokedAt { get; set; }

    [JsonProperty("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }
}
```

---

## API Endpoints

All responses use `application/json`. All request bodies use `application/json` unless noted. All timestamps are ISO 8601 UTC.

### Base URL

```
https://pantrypunk-api.azurewebsites.net/api
```

---

### User Profile

#### `POST /api/users/profile`

Creates or updates the user's profile. Called on first sign-in and when the display name is changed in Settings.

**Auth:** Auth0 JWT

**Request:**
```json
{
  "displayName": "Mike",
  "email": "mike@example.com"
}
```

**Behaviour:**
- Extract `userId` from Auth0 `sub` claim.
- Upsert the `Users` document (create if first sign-in, update if returning).
- If creating: also attempt to create a new empty `ShoppingLists` document for this user. If this write fails, do not return an error to the client — instead, handle the missing list gracefully: `GET /api/shopping-list` checks for the list document and creates an empty one on the fly if it does not exist. This avoids a partial failure leaving the user unable to use the app.
- Trim `displayName` before saving. Return `400` if trimmed value is empty.

**Response `200 OK`:**
```json
{
  "id": "auth0|abc123",
  "displayName": "Mike",
  "isSubscriber": false
}
```

---

#### `GET /api/users/profile`

Returns the current user's profile.

**Auth:** Auth0 JWT

**Response `200 OK`:**
```json
{
  "id": "auth0|abc123",
  "displayName": "Mike",
  "isSubscriber": false
}
```

**Response `404 Not Found`:** User not yet registered — app should call `POST /api/users/profile` first.

---

### Shopping List

> **Authentication note for all list endpoints:** `userId` resolution is handled by authentication middleware before requests reach controllers. For JWT requests, `JwtBearerAuthentication` extracts `userId` from the `sub` claim. For share code requests, `ShareCodeAuthMiddleware` validates the code (checks not expired, not revoked), resolves `ownerUserId`, and injects it as a synthetic claim. Controllers read `userId` from the claims principal — they do not repeat this validation.

#### `GET /api/shopping-list`

Returns the full shopping list for the authenticated user or guest. Called by the app on launch, on foreground resume, and on every 15-second polling tick (when confirmed shared users exist). The app merges the response into its local Hive cache using the List Merge Strategy defined in the frontend spec.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Behaviour:**
1. Determine the caller's identity from the claims principal injected by the authentication middleware:
   - If authenticated via JWT: the `userId` is the Auth0 `sub` claim extracted by `JwtBearerAuthentication`.
   - If authenticated via share code: the `userId` is the `ownerUserId` injected as a synthetic claim by `ShareCodeAuthMiddleware`. The middleware has already validated the share code (not expired, not revoked) before this point — the controller does not need to re-validate it.
2. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container where `ownerUserId == userId` AND (`status` is undefined OR `status == "active"`). Return `404 Not Found` if no active list exists.
3. Map the `ShoppingListDocument` and its embedded `items` array to the response shape.
4. Return `200 OK` with the full list.

> **Performance note:** This endpoint is called frequently (every 15 seconds per active shared-list user). For JWT-authenticated users, the `ShoppingListDocument` is a single-partition-key point read — the most efficient operation in Cosmos DB. For share code guests, `ShareCodeAuthMiddleware` performs a partition-key lookup on the `ShareCodes` container on every request before the controller runs, adding one additional point read. Both are fast at household scale.

**Response `200 OK`:**
```json
{
  "listId": "list-uuid",
  "items": [
    {
      "id": "item-uuid-1",
      "description": "Coles Full Cream Milk (3L)",
      "quantity": 2,
      "addedBy": "Mike",
      "notes": null,
      "photoUrl": "https://pantrypunkstorage.blob.core.windows.net/photos/item-uuid-1.jpg",
      "confidence": "high",
      "createdAt": "2026-04-11T08:00:00Z",
      "updatedAt": "2026-04-11T08:00:00Z"
    }
  ]
}
```

---

#### `POST /api/shopping-list/items`

Adds a new item to the shopping list.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Request:**
```json
{
  "description": "Bananas",
  "quantity": null,
  "notes": null
}
```

> **Note:** `addedBy` is resolved server-side — it is not accepted from the client. For registered users, it is read from `UserDocument.DisplayName`. For guests, it is read from the `RecipientName` claim injected by `ShareCodeAuthMiddleware` — no container lookup is needed.

**Behaviour:**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. Trim `description`. Return `400 Bad Request` if the trimmed value is empty.
3. Trim `notes`. Set to `null` if the trimmed value is empty.
4. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId` as the owner. Return `404 Not Found` if no active list exists for this user (should not occur in normal operation — an active list always exists after first profile save and is replaced by `POST /api/shopping-list/complete`).
5. Generate a new `id` for the item using `Guid.NewGuid().ToString()`.
6. Resolve `addedBy`: for JWT-authenticated users, retrieve `UserDocument.DisplayName` from the `Users` container; for share code guests, read the `RecipientName` claim from the injected claims principal (`User.FindFirst("RecipientName")?.Value`).
7. Construct a new `ShoppingItemDocument` with the provided fields, the generated `id`, resolved `addedBy`, `photoUrl = null`, `confidence = null`, and `createdAt`/`updatedAt` set to `DateTime.UtcNow`.
8. Append the new `ShoppingItemDocument` to the `items` array of the retrieved `ShoppingListDocument`.
9. Update the `updatedAt` field on the `ShoppingListDocument` to `DateTime.UtcNow`.
10. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container using a replace/upsert operation.
11. Return `201 Created` with the newly created `ShoppingItemDocument` serialised as the response body.

**Response `201 Created`:** Returns the created item.

---

#### `POST /api/shopping-list/complete`

Marks the user's active shopping list as completed and creates a fresh active list. Items the shopper didn't buy (named in `unboughtItemIds`) carry over as fresh copies on the new active list; items the shopper bought are flagged `isPurchased = true` on the now-completed document so analytics can read per-trip fulfilment history. Called by the app when the user finishes shopping.

**Auth:** Auth0 JWT or `X-Share-Code` header. Either the owner or a share-code guest can complete (the guest is often the one actually shopping).

**Request:** Body is optional. When provided:
```json
{ "unboughtItemIds": ["<itemId>", "<itemId>"] }
```
Empty body, missing body, or `"unboughtItemIds": []` means the shopper bought every item — the new active list will be empty (matches the legacy behaviour of this endpoint).

**Behaviour:**
1. Read `userId` from the claims principal.
2. Retrieve the active `ShoppingListDocument` (same query as `GET /api/shopping-list`). Return `404 Not Found` if none exists.
3. If the active list has zero items, return `400 Bad Request` with `"Cannot complete an empty list."`.
4. Dedupe `unboughtItemIds`. If any id is not present on the active list's `items`, return `400 Bad Request` with `"Unknown item id(s): ...."` and make no writes.
5. On every item of the active list, set `isPurchased = !unboughtItemIds.Contains(item.id)` and `updatedAt = DateTime.UtcNow`. Items in `unboughtItemIds` keep `isPurchased = false` (they were left behind for the next trip).
6. Build the carry-over set: for each item whose `id` is in `unboughtItemIds`, project to a new `ShoppingItemDocument` with a fresh `id` (`Guid.NewGuid().ToString()`), fresh `createdAt`/`updatedAt`, `isPurchased = false`, and copy `description`, `brand`, `knownAs`, `size`, `quantity`, `notes`, `addedBy`, `addedByMethod`, `photoUrl`, `confidence` verbatim. `photoUrl` is reused as-is — the blob is unaffected.
7. Create a new `ShoppingListDocument` for the same `ownerUserId` with a fresh `id` but **reusing the existing `listId`** (so the new document lands in the same partition as the one being completed). `items =` the carry-over set, `status = "active"`, `createdAt`/`updatedAt = DateTime.UtcNow`. Write it to the container.
8. On the previously active list, set `status = "completed"`, `completedAt = DateTime.UtcNow`, `updatedAt = DateTime.UtcNow`. Replace it in the container — the same write also persists the per-item `isPurchased` flags from step 5.
9. Return `200 OK` with the new active list (same shape as `GET /api/shopping-list`).

> **Why reuse `listId`:** share codes bind to `listId`. Minting a fresh `listId` on each completion would orphan every active share code for that household. Reusing `listId` keeps guest access seamless across trips and leaves share-code bookkeeping untouched.

> **Failure mode:** the order is create-new-first. If step 8 fails after step 7 succeeds, two `status = "active"` documents coexist in the same partition. The active-list query breaks the tie with `ORDER BY c.createdAt DESC`, so the newer list wins and a retry will reject with `400` if it is empty (or proceed normally if it has carried items). Surface the error and log it for operator follow-up to re-mark the older document `completed`.

**Response `200 OK`:** The new active list (containing the carried-over items, or empty if every item was bought), in the same shape as `GET /api/shopping-list`. Each item response includes the new `isPurchased` field.

---

#### `PUT /api/shopping-list/items/:itemId`

Updates an existing item.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Request:**
```json
{
  "description": "Bananas",
  "quantity": 4,
  "notes": "Ripe ones"
}
```

**Behaviour:**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. Trim `description`. Return `400 Bad Request` if the trimmed value is empty.
3. Trim `notes`. Set to `null` if the trimmed value is empty.
4. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId` as the owner. Return `404 Not Found` if no active list exists.
5. Locate the item within the `items` array where `item.id == itemId`. Return `404 Not Found` if no matching item exists.
6. Update the located item's fields: `description`, `quantity`, `notes`. The item's `photoUrl` is preserved; it can only be set by the photo-upload endpoint.
7. Set `updatedAt` on the item to `DateTime.UtcNow`.
8. Set `updatedAt` on the `ShoppingListDocument` to `DateTime.UtcNow`.
9. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container using a replace/upsert operation.
10. Return `200 OK` with the updated `ShoppingItemDocument` serialised as the response body.

**Response `200 OK`:** Returns the updated item.

---

#### `DELETE /api/shopping-list/items/:itemId`

Deletes an item from the list.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Behaviour:**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId` as the owner. Return `404 Not Found` if no active list exists.
3. Locate the item within the `items` array where `item.id == itemId`. Return `404 Not Found` if no matching item exists.
4. Remove the located item from the `items` array.
5. Set `updatedAt` on the `ShoppingListDocument` to `DateTime.UtcNow`.
6. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container using a replace/upsert operation.
7. Return `204 No Content`.

**Response `204 No Content`**

---

#### `DELETE /api/shopping-list/items`

Deletes multiple items from the list in a single request.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Request:**
```json
{
  "itemIds": ["<id>", "<id>", "..."]
}
```

**Behaviour (lenient):**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. If the request body is missing or `itemIds` is `null`, return `400 Bad Request` with the standard `{ error, traceId }` shape.
3. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId` as the owner. Return `404 Not Found` if no active list exists.
4. Build a de-duplicated set of requested IDs (ordinal comparison).
5. Remove every item from the `items` array whose `id` is in the requested set. IDs not present in the list are silently ignored — the client's view may be stale (a co-shopper deleted an item, the list was completed, etc.) and a partially-stale request is not treated as an error.
6. An empty `itemIds` array is a no-op (no items removed).
7. Set `updatedAt` on the `ShoppingListDocument` to `DateTime.UtcNow`.
8. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container using a replace/upsert operation. The write occurs even when zero items matched, keeping behaviour predictable.
9. Return `204 No Content`.

**Response `204 No Content`**

---

#### `POST /api/shopping-list/items/photo`

Uploads a photo to Azure Blob Storage, sends it to the Claude API for recognition, adds the recognised item to the shopping list, and returns the created item with a confidence score. This is a single combined operation — the app makes one call and gets back a fully populated shopping list item.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Request:** `multipart/form-data`, field name `image`. Accepted formats: JPEG, PNG, or WebP. Clients should compress photos to max 1024px / 85% quality before upload.

**Behaviour:**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. Validate the uploaded file via `ImageFileValidator`. All checks below return `400 Bad Request`:
   - File is present and non-empty.
   - File size does not exceed **3MB**.
   - `Content-Type` is one of `image/jpeg`, `image/png`, `image/webp`.
   - The first bytes match the declared `Content-Type`'s magic signature (defends against renamed / disguised files).
   - The bytes decode as a valid image via `SixLabors.ImageSharp.Image.Identify` (defends against malformed / truncated files).
   - Image dimensions do not exceed **8192 × 8192** pixels (defends against decode bombs).
3. Generate a unique blob name using the **detected** extension: `{userId}/{guid}.{jpg|png|webp}`.
5. Upload the image stream directly to the `photos` container in Azure Blob Storage via `BlobStorageService.UploadAsync(image.OpenReadStream())`. Do not load the entire file into memory — stream it directly.
6. Capture the resulting public blob URL.
7. Read the image stream (rewind or re-open) and Base64-encode it.
8. Send the Base64-encoded image to the Claude API with the image recognition system prompt (see Claude API Integration section). Include the confidence self-assessment instruction in the prompt.
9. Parse the Claude response JSON. If parsing fails, delete the uploaded blob and return `422 Unprocessable Entity`.
   - If `confidence` is `"low"`: the item is still created and returned with `confidence: "low"`. The blob is **not** deleted — the URL is stored with the item in case the user later confirms it manually via the Detail Screen. If the user dismisses the low-confidence result without adding the item, the blob remains in storage as an orphan and will be cleaned up by the future scheduled cleanup job.
10. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId`. Return `404 Not Found` if no active list exists.
11. Construct a new `ShoppingItemDocument` with:
    - `id` = `Guid.NewGuid().ToString()`
    - `description` from Claude response
    - `quantity` from Claude response (default `null` if not provided)
    - `addedBy` = for JWT-authenticated users, use `UserDocument.DisplayName` from the `Users` container; for share code guests, read the `RecipientName` claim from the injected claims principal (`User.FindFirst("RecipientName")?.Value`)
    - `notes` = `null`
    - `photoUrl` = blob URL from step 6
    - `confidence` = `"high"`, `"medium"`, or `"low"` from Claude response
    - `createdAt` / `updatedAt` = `DateTime.UtcNow`
12. Append the new item to the `items` array of the `ShoppingListDocument`.
13. Set `updatedAt` on the `ShoppingListDocument` to `DateTime.UtcNow`.
14. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container.
15. Return `201 Created` with the newly created `ShoppingItemDocument`.

**Response `201 Created`:**
```json
{
  "id": "item-uuid",
  "description": "Flora ProActiv Buttery Spread Large Pack 750g",
  "quantity": null,
  "addedBy": "Mike",
  "notes": null,
  "photoUrl": "https://pantrypunkstorage.blob.core.windows.net/photos/auth0|abc123/guid.jpg",
  "confidence": "high",
  "createdAt": "2026-04-11T09:00:00Z",
  "updatedAt": "2026-04-11T09:00:00Z"
}
```

**Response `422 Unprocessable Entity`:**
```json
{
  "error": "Could not recognise item"
}
```

---

### AI — Voice Recognition

#### `POST /api/shopping-list/items/voice`

Accepts an audio recording, transcribes it via Azure AI Speech, extracts shopping items via the Claude API, adds all recognised items to the shopping list, and returns the created items. This is a single combined operation — the app makes one call and gets back fully populated shopping list items ready to display.

**Auth:** Auth0 JWT or `X-Share-Code` header

**Request:** `multipart/form-data`, field name `audio` (m4a, AAC 64kbps 22050Hz, max 3 minutes)

**Behaviour:**
1. Read `userId` from the claims principal injected by authentication middleware (JWT or `ShareCodeAuthMiddleware`). This is available as `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` — no container lookup is needed here.
2. Validate the uploaded file content type is `audio/m4a` or `audio/mp4`. Return `400 Bad Request` for other types.
3. Validate file size does not exceed 2MB (consistent with a 3-minute AAC recording at 64kbps). Return `400 Bad Request` if exceeded.
4. Send the audio stream to **Azure AI Speech** (Speech-to-Text) to produce a plain text transcription.
5. If transcription fails or returns empty text, return `422 Unprocessable Entity` with `{ "error": "Could not transcribe audio" }`.
6. Send the transcribed text to the Claude API with the voice recognition system prompt (see Claude API Integration section) to extract structured shopping items.
7. If Claude returns a response that cannot be parsed as valid JSON, or returns an empty items array, return `422 Unprocessable Entity` with `{ "error": "Could not recognise items from speech" }`.
8. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container using the resolved `userId`. Return `404 Not Found` if no active list exists.
9. Resolve `addedBy`: for JWT-authenticated users, retrieve `UserDocument.DisplayName` from the `Users` container; for share code guests, read the `RecipientName` claim from the injected claims principal (`User.FindFirst("RecipientName")?.Value`).
10. For each item returned by Claude, construct a new `ShoppingItemDocument` with:
    - `id` = `Guid.NewGuid().ToString()`
    - `description` from Claude response (trimmed)
    - `quantity` from Claude response (`null` if not stated)
    - `addedBy` = display name from step 9
    - `notes` = `null`
    - `photoUrl` = `null`
    - `confidence` = `null` (voice items have no image confidence score)
    - `createdAt` / `updatedAt` = `DateTime.UtcNow`
11. Append all new items to the `items` array of the `ShoppingListDocument`.
12. Set `updatedAt` on the `ShoppingListDocument` to `DateTime.UtcNow`.
13. Write the updated `ShoppingListDocument` back to the `ShoppingLists` container using a replace/upsert operation.
14. Return `201 Created` with the array of newly created `ShoppingItemDocument` objects.

**Response `201 Created`:**
```json
{
  "items": [
    {
      "id": "item-uuid-1",
      "description": "Full cream milk",
      "quantity": 2,
      "addedBy": "Mike",
      "notes": null,
      "photoUrl": null,
      "confidence": null,
      "createdAt": "2026-04-11T09:00:00Z",
      "updatedAt": "2026-04-11T09:00:00Z"
    },
    {
      "id": "item-uuid-2",
      "description": "Bananas",
      "quantity": null,
      "addedBy": "Mike",
      "notes": null,
      "photoUrl": null,
      "confidence": null,
      "createdAt": "2026-04-11T09:00:00Z",
      "updatedAt": "2026-04-11T09:00:00Z"
    }
  ]
}
```

**Response `422 Unprocessable Entity`:**
```json
{
  "error": "Could not recognise items from speech"
}
```

---

### Sharing

#### `POST /api/share/generate-code`

Generates a unique share code for a subscriber. The recipient's name is **not** supplied here — the guest provides it when they confirm the code.

**Auth:** Auth0 JWT, `isSubscriber` must be `true` (checked against Cosmos DB)

**Request:** empty body (`{}` or no body).

**Behaviour:**
1. Extract `userId` from the Auth0 JWT `sub` claim.
2. Retrieve the `UserDocument` from the `Users` container using `userId` as the partition key. Return `404 Not Found` if no user document exists.
3. Check `userDocument.IsSubscriber`. If `false`, return `403 Forbidden` with `{ "error": "Sharing requires an active subscription." }`.
4. Retrieve the active `ShoppingListDocument` from the `ShoppingLists` container where `ownerUserId == userId` AND (`status` is undefined OR `status == "active"`). Return `404 Not Found` if no active list exists. The `listId` is a stable per-household identifier that survives completions (see `ShoppingLists` schema notes), so the value captured here remains the partition the share code will resolve into for the rest of its life.
5. Capture the `listId` from the retrieved `ShoppingListDocument`.
6. Generate a 6-character uppercase alphanumeric share code using `RandomNumberGenerator` (e.g. `6Y812C`). Use only characters `A-Z` and `0-9` to avoid ambiguous characters (`0`/`O`, `1`/`I`/`L`).
7. Query the `ShareCodes` container to check whether an active (non-revoked, non-expired) code with the same value already exists. If a collision is found, regenerate and re-check. Retry up to 5 times. If a unique code cannot be generated after 5 attempts, return `409 Conflict` with `{ "error": "Could not generate a unique code. Please try again." }`.
8. Construct a new `ShareCodeDocument`:
    - `id` = `Guid.NewGuid().ToString()`
    - `code` = the generated code from step 6
    - `listId` = from step 5
    - `ownerUserId` = `userId`
    - `recipientName` = `null` (populated at confirmation time)
    - `confirmed` = `false`
    - `confirmedAt` = `null`
    - `revokedAt` = `null`
    - `expiresAt` = `DateTime.UtcNow.AddHours(24)` (configurable via `PantryPunk:ShareCode:ExpiryHours` in the `AppConfig` Cosmos document)
    - `createdAt` = `DateTime.UtcNow`
9. Write the new `ShareCodeDocument` to the `ShareCodes` container.
10. Return `200 OK` with the created share code details.

**Response `200 OK`:**
```json
{
  "shareId": "sharecode-uuid",
  "code": "6Y812C",
  "recipientName": null,
  "confirmed": false,
  "expiresAt": "2026-04-12T08:00:00Z"
}
```

---

#### `POST /api/share/confirm-code`

Non-subscriber submits a code to join a shared list **and provides their own display name**. Public endpoint — no authentication required.

**Request:**
```json
{
  "code": "6Y812C",
  "recipientName": "Natalie"
}
```

**Behaviour:**
- Trim `code` and `recipientName`. Return `400 Bad Request` if either is empty after trimming.
- Look up the `ShareCodeDocument` by `code` (partition key lookup — efficient).
- Return `404` if code not found.
- Return `410 Gone` if `expiresAt` has passed and `confirmed == false`.
- Return `410 Gone` if `revokedAt` is set.
- If valid and not yet confirmed: set `recipientName = <trimmed request value>`, `confirmed = true`, `confirmedAt = DateTime.UtcNow`. Write the updated document back to the `ShareCodes` container.
- If valid and already confirmed (idempotent re-confirm): leave the stored `recipientName` intact — first-confirm wins. The new name in the request is ignored.
- Return `200 OK` with the full `ShareCodeResponse` (same shape as `GET /api/share` items). The `shareId` field is what the guest passes to `DELETE /api/share/:shareId` to leave the list (self-revoke).

**Response `200 OK`:**
```json
{
  "shareId": "sharecode-uuid",
  "recipientName": "Natalie",
  "code": "6Y812C",
  "confirmed": true,
  "confirmedAt": "2026-04-11T10:05:00Z",
  "expiresAt": "2026-04-12T08:00:00Z",
  "createdAt": "2026-04-11T08:00:00Z"
}
```

**Response `410 Gone`:**
```json
{
  "error": "Invalid, expired, or revoked code",
  "traceId": "..."
}
```

---

#### `GET /api/share`

Returns share codes created by the subscriber (for the Share It screen list). Excludes revoked codes and unconfirmed (pending) codes — only codes a guest has actually accepted are returned. Subscribers only — share-code guests are rejected.

**Auth:** Auth0 JWT + isSubscriber

**Behaviour:**
1. Extract `userId` from the Auth0 JWT `sub` claim. Share-code guests (who authenticate via `X-Share-Code`) are rejected by the `RegisteredUser` policy and never reach this handler.
2. Load the caller's `UserDocument` and return `403 Forbidden` with `{ "error": "Sharing requires an active subscription." }` if `isSubscriber` is false.
3. Query the `ShareCodes` container for all documents where `ownerUserId == userId` and `revokedAt == null`. (Note: the container is partitioned by `/code`, so this is a cross-partition query scoped by `ownerUserId`. At household scale this is negligible — typically < 10 documents.)
4. Filter out documents where `confirmed == false` (pending/unaccepted codes are not returned).
5. Map each remaining `ShareCodeDocument` to the response shape.
6. Return `200 OK` with the array sorted by `createdAt` ascending.

**Response `200 OK`:**
```json
{
  "sharedUsers": [
    {
      "shareId": "sharecode-uuid",
      "recipientName": "Natalie",
      "code": "6Y812C",
      "confirmed": true,
      "confirmedAt": "2026-04-11T10:05:00Z",
      "expiresAt": "2026-04-12T08:00:00Z",
      "createdAt": "2026-04-11T08:00:00Z"
    }
  ]
}
```

Only confirmed codes are returned, so `confirmed` is always `true` and `recipientName` is always populated. Unconfirmed codes remain retrievable via `POST /api/share/generate-code` (which returns the freshly generated code) but do not appear in this list until a guest confirms them.

---

#### `DELETE /api/share/:shareId`

Revokes a share code. The associated guest loses list access on their next API call. Accepts two caller modes:

1. **Subscriber (Auth0 JWT + `isSubscriber`):** may revoke any share code they own.
2. **Share-code guest (`X-Share-Code` header):** may revoke **only** the share code they authenticated with. This is a "leave this list" action.

**Auth:** Auth0 JWT + isSubscriber, **or** `X-Share-Code` (self only)

**Behaviour:**
- If the caller is a share-code guest, compare the route `shareId` against the document Id of the share code they authenticated with. Return `403 Forbidden` with `{ "error": "You can only revoke your own share code." }` if they differ.
- If the caller is a JWT user, load their `UserDocument` and return `403 Forbidden` with `{ "error": "Sharing requires an active subscription." }` if `isSubscriber` is false.
- The `ShareCodes` container is partitioned by `/code`, not `/id`. A direct point-read by `shareId` alone would require a cross-partition query. To avoid this, perform a query scoped to the owner: `SELECT * FROM c WHERE c.id = @shareId AND c.ownerUserId = @userId`. For share-code guests the `userId` is the owner's id (injected by the middleware), so the ownership check is preserved.
- Return `404 Not Found` if no document with the given `shareId` exists for this owner.
- Set `revokedAt = DateTime.UtcNow`.
- Write the updated `ShareCodeDocument` back to the container.
- Do not delete the document — soft delete for audit trail.

> **Future optimisation:** If this endpoint is called frequently, consider adding a secondary index or storing `shareId` alongside `code` in a lookup structure. At household scale a cross-user query is negligible.

**Response `204 No Content`**

---

### Feature Flags Endpoint

#### `GET /api/features`

Returns the current set of feature flags for the authenticated user. Called by the Flutter app on launch and on foreground resume. The backend evaluates each flag against the user's context (user ID, subscription status) using `IFeatureManager` and returns the resolved values.

**Auth:** Auth0 JWT

**Response `200 OK`:**
```json
{
  "talkIt": true,
  "realtimeSync": false,
  "annualSubscription": false,
  "appAttest": false
}
```

**Behaviour:**
- Evaluate each flag via `IFeatureManager.IsEnabledAsync(flagName, userContext)` where `userContext` contains the Auth0 `userId` and `isSubscriber` status.
- Return all known flags in a single flat object — the app does not call this endpoint per-flag.
- Flags not explicitly defined in the `AppConfig` Cosmos document default to `false`.
- Response is cached per-user for 60 seconds to avoid hammering Cosmos on every foreground resume.

---

### Subscriptions (RevenueCat Webhook)

#### `POST /api/webhooks/revenuecat`

Receives subscription lifecycle events from RevenueCat. Not protected by Auth0 — protected by RevenueCat webhook signature verification.

**Auth:** RevenueCat webhook signature header `X-RevenueCat-Signature`

**Behaviour:**
- Verify the webhook signature using the RevenueCat webhook secret from Key Vault.
- Extract `app_user_id` (Auth0 `sub`) and `event.type` from the payload.
- Look up the user in the `Users` container by `userId = app_user_id`.
- Apply the relevant state change (see table below).
- Return `200 OK` immediately — RevenueCat retries on any non-200 response.

| Event type | Action |
|---|---|
| `INITIAL_PURCHASE` | Set `isSubscriber = true`, `subscribedAt = now` |
| `RENEWAL` | Set `isSubscriber = true` |
| `CANCELLATION` | Set `subscriptionExpiresAt` from payload end date |
| `EXPIRATION` | Set `isSubscriber = false`, revoke all active share codes for this user |
| `BILLING_ISSUE` | Log the event; optionally set a grace period flag (TBD) |
| `SUBSCRIBER_ALIAS` | Update `userId` mapping if needed |

> **Note on CANCELLATION vs EXPIRATION:** RevenueCat fires `CANCELLATION` when a user cancels but the subscription period has not yet ended. Access should remain active until `EXPIRATION` fires at the end of the paid period. Do **not** revoke access on `CANCELLATION` — only on `EXPIRATION`.

**Response `200 OK`** (always, even for unhandled event types — log and ignore unknown events)

---

## Claude API Integration

### Overview

The Claude API is called for two purposes:
- **Image recognition:** Identify a grocery item from a photo
- **Voice recognition:** Extract shopping list items from an audio transcription

Use the Anthropic .NET SDK (`Anthropic.SDK`) or a typed `HttpClient` wrapping the `POST https://api.anthropic.com/v1/messages` endpoint. The API key is stored in Azure Key Vault.

### Image Recognition Prompt

```csharp
var systemPrompt = """
    You are a grocery item recognition assistant.
    When given an image of a grocery product, respond ONLY with a JSON object
    containing the following fields:
    - description: the full product name including brand, variant, and size (string)
    - brand: the brand name only (string or null if not identifiable)
    - quantity: suggested quantity to add to a shopping list (integer or null if unclear)
    - confidence: your confidence in the identification ("high", "medium", or "low")

    Confidence guidelines:
    - "high": brand, product name, and size are all clearly visible and identified
    - "medium": product type is clear but brand or size is uncertain or partially obscured
    - "low": image is unclear, partially visible, or the product cannot be reliably identified

    Example response:
    {"description":"Flora ProActiv Buttery Spread 750g","brand":"Flora","quantity":null,"confidence":"high"}

    Do not include any other text, explanation, or markdown. JSON only.
    """;

var userMessage = new
{
    type = "image",
    source = new
    {
        type = "base64",
        media_type = mediaType, // "image/jpeg" | "image/png" | "image/webp" — determined by ImageFileValidator
        data = Convert.ToBase64String(imageBytes)
    }
};
```

**Model:** `claude-sonnet-4-6` — use Sonnet for cost efficiency; switch to Opus if recognition quality is insufficient.

**Max tokens:** `256` (JSON response is always short)

**Error handling:** If Claude returns a response that cannot be parsed as valid JSON, or if `confidence` is missing or not one of the three expected values, default `confidence` to `"low"` and attempt to use any other fields that did parse successfully. If `description` is also missing, return `422` to the client and delete the uploaded blob.

### Voice Recognition Prompt

```csharp
var systemPrompt = """
    You are a shopping list voice assistant.
    The user has spoken a message describing items they want to add to their shopping list.
    Extract all shopping items mentioned and respond ONLY with a JSON object:
    {
      "items": [
        { "description": "item name", "quantity": number_or_null }
      ]
    }

    Rules:
    - Normalise item names (e.g. "2 litres of full cream milk" → description: "Full cream milk", quantity: 2)
    - If no quantity is mentioned, set quantity to null
    - Include all distinct items mentioned
    - Do not include any other text or markdown. JSON only.
    """;
```

**Model:** `claude-sonnet-4-6`

**Max tokens:** `512`

**Note:** Claude does not natively accept audio input. The audio must first be transcribed to text. Options:
- Use **Azure AI Speech** (Speech-to-Text) to transcribe the m4a to text, then pass the text to Claude for entity extraction
- Alternatively, use Claude's vision capability with an audio transcription service of your choice

Recommended approach: **Azure AI Speech → text → Claude** for entity extraction. This separates concerns and keeps costs predictable.

---

## RevenueCat Webhook Handling

### Signature Verification

```csharp
[HttpPost("revenuecat")]
public async Task<IActionResult> RevenueCat(
    [FromBody] JsonDocument payload,
    [FromHeader(Name = "X-RevenueCat-Signature")] string signature)
{
    var secret = _keyVault.GetSecret("RevenueCat--WebhookSecret");
    var body = await new StreamReader(Request.Body).ReadToEndAsync();
    var expectedSignature = ComputeHmacSha256(body, secret);

    if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(signature),
        Encoding.UTF8.GetBytes(expectedSignature)))
    {
        return Unauthorized();
    }

    // process event
}
```

Use `CryptographicOperations.FixedTimeEquals` (constant-time comparison) to prevent timing attacks on the signature check.

---

## Runtime Configuration (AppConfig)

### Overview

Runtime configuration and feature flags live in a single Cosmos document in the `AppConfig` container (id `app-config`, partition key `/id`). The doc holds a flat `settings` dictionary keyed by `IConfiguration` paths (e.g. `PantryPunk:Claude:Model`, `FeatureManagement:TalkIt:EnabledFor:0:Name`).

`AppConfigMiddleware` reads the doc on each request (with a 30-second in-memory cache, fail-open with last-known-good on Cosmos errors), and publishes the dictionary to an `AsyncLocal` slot on `AmbientConfigurationProvider`. That provider is registered last in the global `IConfigurationRoot` chain, so its values override anything in `appsettings.json` or App Service env vars for every consumer that resolves `IConfiguration` per request — including `Microsoft.FeatureManagement`.

Secrets (Claude API key, RevenueCat webhook secret, Azure Speech key/region) live in Azure Key Vault and are loaded into `IConfiguration` once at app startup via `Azure.Extensions.AspNetCore.Configuration.Secrets`. Bootstrap-only values (Cosmos endpoint, database name, blob account, Auth0 domain/audience, Key Vault URI) live in App Service env vars — they cannot live in the AppConfig doc because we need them in order to read the doc.

### NuGet Packages

```xml
<PackageReference Include="Microsoft.FeatureManagement.AspNetCore" Version="*" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="*" />
```

### Bootstrap in `Program.cs`

```csharp
// Loads Key Vault secrets into IConfiguration if KeyVault:Uri is set; no-op otherwise.
builder.AddKeyVaultSecrets();

// Per-request overlay populated by AppConfigMiddleware. Sits last in the chain so its values win.
((IConfigurationBuilder)builder.Configuration).Add(new AmbientConfigurationProvider());

builder.Services.AddMemoryCache();
builder.Services.AddFeatureManagement();
```

In the request pipeline:

```csharp
app.UseMiddleware<AppConfigMiddleware>(); // must run before anything that reads PantryPunk:* / FeatureManagement:*
```

### Configuration Keys

Stored in the `settings` dictionary on the `app-config` document.

| Key | Type | Example Value | Purpose |
|---|---|---|---|
| `PantryPunk:Claude:Model` | String | `claude-sonnet-4-6` | Claude model to use for AI features |
| `PantryPunk:Claude:MaxTokensImage` | Integer | `256` | Max tokens for image recognition responses |
| `PantryPunk:Claude:MaxTokensVoice` | Integer | `512` | Max tokens for voice recognition responses |
| `PantryPunk:RateLimit:AiRequestsPerMinute` | Integer | `30` | Max AI requests per user per minute (startup-frozen — see note below) |
| `PantryPunk:RateLimit:ShareConfirmPerHour` | Integer | `10` | Per-IP confirm rate (startup-frozen) |
| `PantryPunk:RateLimit:PerIpPerMinute` | Integer | `120` | Global per-IP rate (startup-frozen) |
| `PantryPunk:ShareCode:ExpiryHours` | Integer | `24` | Hours before an unconfirmed share code expires |
| `FeatureManagement:TalkIt:*` | Targeting filter | — | Enables the Talk It voice recording feature |
| `FeatureManagement:RealtimeSync:*` | Targeting filter | — | Enables WebSocket real-time list sync |
| `FeatureManagement:AnnualSubscription:*` | Targeting filter | — | Shows annual plan option on Paywall screen |
| `FeatureManagement:AppAttest:*` | Targeting filter | — | Enables App Attest / Play Integrity enforcement |

> **Rate-limit values are startup-frozen.** `AddRateLimiter` partition factories capture the values at app build time. Editing rate-limit keys in the AppConfig doc has no effect until the App Service restarts. They live in the doc for visibility/documentation only; treat them as deploy-time config.

The canonical seed document lives at `infra/seed/app-config.json` and is auto-seeded into Cosmos on first deploy by the `seedAppConfig` Bicep module (create-if-not-exists; existing documents are left untouched on re-deploy).

### Feature Flag Definitions

Flags use the standard `Microsoft.FeatureManagement` schema, expressed as flat keys under `FeatureManagement:`. Each flag supports the same filters as before — `Microsoft.Targeting` for percentage rollout and group-based targeting is wired in the seed doc:

```jsonc
"FeatureManagement:TalkIt:EnabledFor:0:Name": "Microsoft.Targeting",
"FeatureManagement:TalkIt:EnabledFor:0:Parameters:Audience:DefaultRolloutPercentage": "0",
"FeatureManagement:TalkIt:EnabledFor:0:Parameters:Audience:Groups:0:Name": "subscribers",
"FeatureManagement:TalkIt:EnabledFor:0:Parameters:Audience:Groups:0:RolloutPercentage": "0"
```

#### Defined Flags

| Flag name | Default | Description |
|---|---|---|
| `TalkIt` | `false` | Voice recording and transcription via Talk It screen |
| `RealtimeSync` | `false` | WebSocket-based real-time list sync (replaces polling) |
| `AnnualSubscription` | `false` | Annual subscription product shown on Paywall screen |
| `AppAttest` | `false` | App Attest (iOS) and Play Integrity (Android) enforcement |

#### Targeting Context

Pass user context to `IFeatureManager` so targeting rules can be evaluated per-user:

```csharp
public class UserTargetingContext : ITargetingContext
{
    public string UserId { get; set; }
    public IEnumerable<string> Groups { get; set; }
}

// In FeatureFlagService:
var context = new UserTargetingContext
{
    UserId = userId,
    Groups = isSubscriber ? ["subscribers"] : ["free"]
};

var isEnabled = await _featureManager.IsEnabledAsync("TalkIt", context);
```

This allows flags to be targeted at the `subscribers` group (e.g. roll out a new feature to paying users first).

### Checking Flags in Controllers/Services

```csharp
// Guard an entire endpoint
if (!await _featureManager.IsEnabledAsync("TalkIt"))
    return StatusCode(503, new { error = "Feature not available" });

// Guard a code path in a service
if (await _featureManager.IsEnabledAsync("RealtimeSync"))
{
    // use WebSocket path
}
else
{
    // use polling path
}
```

### Local Development

For local development, run the API without an `AppConfig` Cosmos document — the middleware fails open and `IConfiguration` falls through to `appsettings.Development.json`:

```json
{
  "FeatureManagement": {
    "TalkIt": true,
    "RealtimeSync": false,
    "AnnualSubscription": true,
    "AppAttest": false
  },
  "PantryPunk": {
    "Claude": {
      "Model": "claude-sonnet-4-6",
      "MaxTokensImage": 256,
      "MaxTokensVoice": 512
    },
    "RateLimit": {
      "AiRequestsPerMinute": 30
    },
    "ShareCode": {
      "ExpiryHours": 24
    }
  }
}
```

To exercise the overlay end-to-end against the Cosmos Emulator, create an `AppConfig` container with partition key `/id` and paste `infra/seed/app-config.json`.

---

## File Storage

### Azure Blob Storage Configuration

- **Storage account:** `pantrypunkstorage`
- **Container:** `photos` (public read access — URLs are embedded in list items)
- **Blob naming:** `{userId}/{guid}.jpg` — the GUID is generated at upload time, before the item document exists. The resulting URL is stored on the item once it is created.
- **Access:** App Service accesses blob storage via managed identity using `BlobServiceClient` with `DefaultAzureCredential`

### Photo Lifecycle

- Photos are uploaded when `POST /api/shopping-list/items/photo` is called.
- When an item is deleted, the associated blob is **not** deleted immediately — implement a scheduled cleanup job as a future task to remove orphaned blobs.
- Photo URLs are permanent once issued (no SAS tokens needed for public read containers).

---

## Error Handling

### Global Exception Handler

Register a global exception handling middleware in `Program.cs` using `app.UseExceptionHandler`. All unhandled exceptions return:

```json
{
  "error": "An unexpected error occurred.",
  "traceId": "<request-trace-id>"
}
```

Never expose stack traces or internal exception messages in production responses.

### Standard Error Response Shape

All error responses use a consistent shape:

```csharp
public class ErrorResponse
{
    public string Error { get; set; }
    public string? TraceId { get; set; }
}
```

### HTTP Status Code Usage

| Status | When to use |
|---|---|
| `200 OK` | Successful GET, PUT |
| `201 Created` | Successful POST that creates a resource |
| `204 No Content` | Successful DELETE |
| `400 Bad Request` | Validation failure (missing/invalid fields, wrong file type, file too large) |
| `401 Unauthorised` | Missing or invalid auth token |
| `403 Forbidden` | Valid token but insufficient permissions (e.g. non-subscriber accessing share features) |
| `404 Not Found` | Resource not found |
| `409 Conflict` | Duplicate resource (e.g. share code collision after retries exhausted) |
| `410 Gone` | Expired or revoked share code |
| `422 Unprocessable Entity` | AI could not produce a usable result — `description` missing from Claude response, or audio transcription failed entirely. Note: low confidence is **not** a 422 — the item is still returned with `confidence: "low"` and a `201 Created`. |
| `500 Internal Server Error` | Unhandled exception |

---

## Logging & Monitoring

### Application Insights

All telemetry flows to Azure Application Insights via the `Microsoft.ApplicationInsights.AspNetCore` package.

Register in `Program.cs`:
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

**Log the following as custom events or traces:**
- Every Claude API call (duration, model used, token count, success/failure)
- Every image recognition result (confidence level, duration) — use this data to tune the Claude prompt over time
- Every RevenueCat webhook received (event type, userId, outcome)
- Every share code generated and confirmed
- Every `403` or `422` response (to detect abuse or AI failures)
- Every `confidence: "low"` result (to monitor recognition quality)

**Do not log:**
- Request/response bodies containing user data (PII)
- Auth tokens or share codes in plain text
- Photo content

### Structured Logging

Use `ILogger<T>` throughout with structured logging:

```csharp
_logger.LogInformation(
    "ImageRecognition completed. UserId: {UserId}, Duration: {DurationMs}ms, Success: {Success}",
    userId, duration.TotalMilliseconds, success);
```

---

## Security

- **HTTPS only** — enforced at App Service level and via `app.UseHttpsRedirection()`
- **CORS** — restrict to the app's bundle identifier / known origins only. Do not use wildcard `*` in production.
- **Rate limiting** — apply rate limiting middleware (`Microsoft.AspNetCore.RateLimiting`) on AI endpoints (`/api/shopping-list/items/photo`, `/api/shopping-list/items/voice`) to protect Claude API costs. Suggested: 30 requests per user per minute.
- **Input validation** — use Data Annotations or FluentValidation on all request DTOs. Reject requests with oversized payloads.
- **Max upload size** — enforce a 3MB limit on photo uploads (audio still capped at 2MB in the voice controller) via Kestrel in `Program.cs`:
  ```csharp
  builder.WebHost.ConfigureKestrel(options =>
      options.Limits.MaxRequestBodySize = 3 * 1024 * 1024); // 3MB
  ```
- **Secrets** — all secrets (Cosmos DB connection string, Claude API key, Auth0 credentials, RevenueCat webhook secret) stored in Azure Key Vault. Never committed to source control.
- **Soft deletes** — share codes use soft delete (`revokedAt`) for audit trail. Do not hard-delete security-relevant documents.
- **Cosmos DB** — use the principle of least privilege. The App Service managed identity has `Cosmos DB Built-in Data Contributor` role only — not account-level access.

---

## Development & Deployment

### Local Development

- Use **Azure Cosmos DB Emulator** for local database development
- Use **Azurite** for local blob storage emulation
- Override feature flags and configuration in `appsettings.Development.json` (the AppConfig middleware fails open when no Cosmos document is present), or seed an `AppConfig` container in the Emulator from `infra/seed/app-config.json` to exercise the overlay path end-to-end (see Runtime Configuration section)
- Store local secrets in `appsettings.Development.json` (git-ignored) or .NET User Secrets (`dotnet user-secrets`)
- Set `ASPNETCORE_ENVIRONMENT=Development` locally

### Configuration Shape (`appsettings.json`)

`appsettings.json` contains only the bootstrapping values needed to connect to Azure services. `PantryPunk:*` and `FeatureManagement:*` runtime keys come from the `AppConfig` Cosmos document via the per-request overlay. Secrets come from Azure Key Vault, loaded once at startup via managed identity.

```json
{
  "Auth0": {
    "Domain": "<your-auth0-domain>",
    "Audience": "<your-auth0-api-audience>"
  },
  "KeyVault": {
    "Uri": "https://<vault-name>.vault.azure.net/"
  },
  "CosmosDb": {
    "AccountEndpoint": "https://<account>.documents.azure.com:443/",
    "DatabaseName": "PantryPunkDb"
  },
  "BlobStorage": {
    "AccountName": "pantrypunkstorage",
    "PhotosContainer": "photos"
  },
  "ApplicationInsights": {
    "ConnectionString": "<from-key-vault>"
  }
}
```

> All `PantryPunk:*` and `FeatureManagement:*` keys (Claude model, rate limits, share code expiry, feature flags) live in the `AppConfig` Cosmos document — not in `appsettings.json`. The Claude API key, RevenueCat webhook secret, and Azure Speech key/region live in Key Vault and are loaded at startup.

### Azure DevOps Pipeline

Two pipelines:

**CI (`azure-pipelines-ci.yml`):**
- Trigger: PR to `main`
- Steps: restore → build → test → publish build artefact

**CD (`azure-pipelines-cd.yml`):**
- Trigger: merge to `main`
- Steps: download artefact → deploy to App Service slot → smoke test → swap slot to production

### Health Check

Register a health check endpoint for App Service:

```csharp
builder.Services.AddHealthChecks()
    .AddCosmosDb(cosmosConnectionString)
    .AddAzureBlobStorage(storageConnectionString);

app.MapHealthChecks("/health");
```

App Service health check path: `/health`
