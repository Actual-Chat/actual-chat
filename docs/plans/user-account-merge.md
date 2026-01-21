# User and Account Merge Plan

## Overview

This document outlines the plan to merge the `User` and `Account` types into a single unified `User` type. The current `Account`/`AccountsBackend` will be renamed to `Users`/`UsersBackend`.

## Current State

### Type Hierarchy

```
User (Core/Users/User.cs)
├── Id: string
├── Name: string
├── Version: long
├── Claims: ApiMap<string, string>        # To be removed
└── Identities: ApiMap<UserIdentity, string>

Account (Api/Users/Account.cs)
├── Id: UserId
├── Version: long
├── Status: AccountStatus
└── Avatar: Avatar

AccountFull : Account (Api/Users/AccountFull.cs)
├── User: User                            # Nested User object
├── IsAdmin: bool
├── SyncContacts: bool
├── Phone: Phone?
├── Email: string
├── Name: string                          # Duplicated from User
├── Username: string
├── IsGreetingCompleted: bool
├── IsEmailVerified: bool
├── CreatedAt: Moment
├── TimeZone: string
└── AliasId: AliasId?
```

### Database Schema

```
DbUser (users table)
├── Id (PK)
├── Version
├── Name
├── Claims (JSON)
└── CreatedAt

DbUserIdentity (user_identities table)
├── Id (PK)
├── DbUserId (FK → users.id)
└── Secret

DbAccount (accounts table)
├── Id (PK, matches users.id)
├── Version
├── Status
├── Email, IsEmailVerified
├── Phone
├── Name                                  # Duplicated from users
├── Username, UsernameNormalized
├── IsGreetingCompleted
├── TimeZone
├── AliasId
└── CreatedAt                             # Duplicated from users
```

## Target State

### Type Hierarchy

The new type hierarchy mirrors the current Account/AccountFull pattern:

```
User (public/trimmed data - like current Account)
├── Id: UserId
├── Version: long
├── Status: UserStatus
└── Avatar: Avatar

UserFull : User (complete data - like current AccountFull)
├── All User properties
├── Identities: ApiMap<UserIdentity, string>
├── Profile: Name, Username, Email, Phone, AliasId
├── Settings: IsAdmin, SyncContacts, TimeZone, IsGreetingCompleted
├── Verification: IsEmailVerified
└── Metadata: CreatedAt
```

### User Type (Public/Trimmed Data)

Used when viewing other users - minimal safe-to-share information:

```csharp
// Similar to current Account - public view of a user
public partial record User(
    UserId Id,
    long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    // Status
    public UserStatus Status { get; init; }
    public Avatar Avatar { get; init; }

    // Computed
    public bool IsGuest => Id.IsGuest;
}
```

### UserFull Type (Complete Data)

Returned by UsersBackend - contains all user information:

```csharp
// Similar to current AccountFull - complete user data
public sealed partial record UserFull(
    UserId Id,
    long Version = 0
) : User(Id, Version)
{
    // Identity (from old User type)
    public ApiMap<UserIdentity, string> Identities { get; init; }

    // Profile
    public string Name { get; init; } = "";
    public string Username { get; init; } = "";
    public Phone? Phone { get; init; }
    public string Email { get; init; } = "";
    public AliasId? AliasId { get; init; }

    // Verification
    public bool IsEmailVerified { get; init; }

    // Settings
    public bool IsAdmin { get; init; }
    public bool SyncContacts { get; init; }
    public bool IsGreetingCompleted { get; init; }
    public string TimeZone { get; init; } = "";

    // Metadata
    public Moment CreatedAt { get; init; }
}
```

### Type Usage Pattern

| Context | Type | Example |
|---------|------|---------|
| Backend service returns | `UserFull` | `IUsersBackend.Get()` → `UserFull?` |
| Viewing other users | `User` | `IUsers.Get(session, userId)` → `User?` |
| Own user data | `UserFull` | `IUsers.GetOwn(session)` → `UserFull` |
| User lists/search | `User` | Public info only |

### What's Removed

- **Claims** (`ApiMap<string, string>`): Not needed - all useful claims are extracted to dedicated properties (Email, Phone, roles via IsAdmin)
- **Old Core User type**: Replaced by UserFull
- **Nested User object in AccountFull**: Data is now flattened into UserFull
- **Duplicate Name/CreatedAt**: Single source of truth in UserFull

## Database Migration

### Phase 1: Schema Preparation

1. **Add missing columns to `accounts` table** (if any)
2. **Migrate data from `users` table to `accounts`**:
   - Identities are already in separate table (user_identities)
   - Claims can be dropped (not needed)

3. **Rename tables**:
   ```sql
   ALTER TABLE accounts RENAME TO users_new;
   ALTER TABLE users RENAME TO users_old;
   ALTER TABLE users_new RENAME TO users;
   ```

### Phase 2: Merge Tables

Option A: **Keep accounts as the main table, drop users table**
- `accounts` already has all profile data
- `user_identities` FK already points to the same ID
- Just need to ensure `accounts.id` is used consistently

Option B: **Merge into a single new table**
- Create new `users` table with combined schema
- Migrate data from both tables
- Update `user_identities` FK

**Recommended: Option A** - Less data movement, accounts already has most fields.

### Migration Script Outline

```sql
-- Step 1: Ensure all users have accounts (should already be true)
INSERT INTO accounts (id, version, status, name, created_at, ...)
SELECT id, version, 'Active', name, created_at, ...
FROM users u
WHERE NOT EXISTS (SELECT 1 FROM accounts a WHERE a.id = u.id);

-- Step 2: Drop claims from users (or just stop using the column)
-- Claims data is not needed in new schema

-- Step 3: Rename tables
ALTER TABLE users RENAME TO _users_deprecated;
ALTER TABLE accounts RENAME TO users;

-- Step 4: Update user_identities FK (if needed)
-- FK should still work since IDs match

-- Step 5: Drop old table after verification
-- DROP TABLE _users_deprecated;
```

## Code Migration

### Phase 1: Rename Types

| Old Name | New Name | Notes |
|----------|----------|-------|
| `Account` | `User` | Public/trimmed user data |
| `AccountFull` | `UserFull` | Complete user data |
| `AccountStatus` | `UserStatus` | Enum rename |
| `IAccounts` | `IUsers` | Frontend service interface |
| `IAccountsBackend` | `IUsersBackend` | Backend service interface |
| `Accounts` | `Users` | Frontend service implementation |
| `AccountsBackend` | `UsersBackend` | Backend service implementation |
| `DbAccount` | `DbUser` | Merge with existing DbUser |
| Old `User` (Core) | — | Deleted, replaced by UserFull |

### Phase 2: Remove Old User Type (Core/Users/User.cs)

The old `User` type in Core project is eliminated:

1. Delete `Core/Users/User.cs`
2. `UserFull` takes over as the complete user model
3. `UserFull.Identities` replaces `User.Identities`
4. `User.Claims` is dropped entirely (not needed)
5. Update `ToModel()` methods in DB entities to return `UserFull`

### Phase 3: Update Services

1. **Auth flow** (`ServerAuth.cs`):
   - `CreateOrUpdateUser()` → creates/updates `UserFull` directly
   - No intermediate old-User object creation
   - Identity data goes directly into `UserFull.Identities`

2. **Sign-in command**:
   - `UsersBackend_SignIn` takes identity info, creates `UserFull`
   - Single entity creation (no separate User + Account)

3. **Session info**:
   - `SessionAuthInfo.UserId` stays the same
   - `Auth.GetUser()` returns `UserFull?` (or `User?` for trimmed version)

4. **Conversion methods**:
   - `UserFull.ToUser()` → converts to trimmed `User` (like current `AccountFull.ToAccount()`)

### Phase 4: Update Contracts

1. `User` and `UserFull` stay in `Api` project (like Account/AccountFull)
2. Update all `IAccounts` → `IUsers` method signatures:
   ```csharp
   // IUsers (frontend)
   Task<UserFull> GetOwn(Session session, ...);
   Task<User?> Get(Session session, UserId userId, ...);

   // IUsersBackend (backend)
   Task<UserFull?> Get(UserId userId, ...);
   ```
3. Update command types:
   - `Accounts_Update` → `Users_Update`
   - `Accounts_DeleteOwn` → `Users_DeleteOwn`
   - `AccountsBackend_Update` → `UsersBackend_Update`
   - `AccountsBackend_SignIn` → `UsersBackend_SignIn`
   - etc.

## Files to Modify

### Core Changes
- `src/dotnet/Core/Users/User.cs` - Delete or completely rewrite
- `src/dotnet/Core/Users/UserId.cs` - Keep as-is
- `src/dotnet/Core/Users/UserIdentity.cs` - Keep as-is
- `src/dotnet/Core/Users/IAuth.cs` - Update return types

### Api Changes
- `src/dotnet/Api/Users/Account.cs` → Rename to `User.cs`
- `src/dotnet/Api/Users/AccountFull.cs` → Delete or merge
- `src/dotnet/Api/Users/AccountStatus.cs` → Rename to `UserStatus.cs`
- `src/dotnet/Api/Users/AccountExt.cs` → Rename to `UserExt.cs`

### Contracts Changes
- `src/dotnet/Api.Contracts/Users/IAccounts.cs` → Rename to `IUsers.cs`
- `src/dotnet/Users.Contracts/IAccountsBackend.cs` → Rename to `IUsersBackend.cs`
- Update all command types (e.g., `Accounts_Update` → `Users_Update`)

### Service Changes
- `src/dotnet/Users.Service/Accounts.cs` → Rename to `Users.cs`
- `src/dotnet/Users.Service/AccountsBackend.cs` → Rename to `UsersBackend.cs`
- `src/dotnet/Users.Service/Db/DbAccount.cs` → Merge with `DbUser.cs`
- `src/dotnet/Users.Service/Db/DbUser.cs` → Update to new schema
- `src/dotnet/Users.Service/ServerAuth.cs` - Update auth flow
- `src/dotnet/Users.Service/Auth.cs` - Update return types

### Migration
- `src/dotnet/Users.Service.Migration/` - Add migration for schema changes

## Migration Order

1. **Create new unified `User` type** alongside existing types
2. **Create database migration** to merge tables
3. **Update DbUser/DbAccount** to work with new schema
4. **Update services** to use new types
5. **Update contracts and interfaces**
6. **Remove old types** (User, Account, AccountFull)
7. **Rename** Accounts → Users throughout

## Risks and Considerations

1. **Breaking API changes**: All Account references become User references
2. **Database migration**: Need careful handling of existing data
3. **Guest users**: Ensure guest user flow still works without full User record
4. **External integrations**: Any external systems using Account API need updates
5. **Caching/Invalidation**: Ensure computed method invalidation still works

## Testing Strategy

1. Run all existing integration tests after each phase
2. Specific tests for:
   - User sign-in/sign-out flow
   - User profile updates
   - Guest user handling
   - Admin operations
   - Contact sync with users

## Open Questions

1. Do we need `User` vs `UserFull` distinction, or just one `User` type?
2. Should `User` stay in `Core` project or move to `Api`?
3. Timeline for deprecation of old API endpoints?
