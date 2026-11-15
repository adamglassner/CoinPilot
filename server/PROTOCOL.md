# WillowWallet sync protocol (version 1)

How WillowWallet for Windows and WillowWallet for Android talk to WillowWallet Server. The reference code is
[`WillowWallet.Sync`](WillowWallet.Sync) (C#, used by the server and the Windows app) and
[`mobile/lib/sync`](../mobile/lib/sync) (Dart). [`test-vectors.json`](test-vectors.json) has fixed inputs and outputs
that both are tested against.

## Encryption

Everything a device sends about your money is sealed on the device. The server only sees usernames, record ids,
collection names, timestamps and sizes.

| Step | How |
| --- | --- |
| Password → secrets | PBKDF2-HMAC-SHA256(password, random 16-byte salt, iterations; default 600,000, server accepts 100,000–10,000,000) → 64 bytes. The first 32 are the **auth secret** (sent to the server, which stores a salted hash of it). The last 32 are the **key-encryption key** (never leaves the device). |
| Data key | 32 random bytes made when the account is created. Stored on the server only wrapped: `Seal(KEK, dataKey, "ww-key-v1")`. |
| Seal | AES-256-GCM with a random 12-byte nonce and the given associated data. Written as `"v1." + base64(nonce ‖ ciphertext ‖ 16-byte tag)`. |
| Records | `Seal(dataKey, UTF-8 JSON, "ww-rec-v1:{collection}:{id}")`, so a sealed record can't be moved to another id. |
| Files | The same, as raw bytes (`nonce ‖ ciphertext ‖ tag`), with `"ww-file-v1:{name}"`. |
| Recovery key | 20 random bytes, shown as base32 in groups of four (`GIZT-INJW-…`). HKDF-SHA256 (empty salt) gives a recovery auth secret (info `ww-recovery-auth`) and a recovery KEK (info `ww-recovery-kek`). The server stores a hash of the first and the data key sealed with the second. |

Changing the password re-wraps the same data key, so nothing has to be encrypted again. Using the recovery key
sets a new password the same way.

## API

JSON over HTTP(S), camelCase, under `/api/v1`. Errors are `{"error": "<code>", "message": "<sentence a person can read>"}`
with a 4xx/5xx status. Signed-in calls send `Authorization: Bearer <token>`; a token belongs to one device and lasts
180 days from its last use. `401` means the device was signed out (password changed, recovery used, or the admin
signed it out). `503` with `paused` means the admin stopped syncing; devices keep their changes and try later.

| Call | Body → answer |
| --- | --- |
| `GET /info` | → `name, version, openSignup, syncing, protocolVersion` |
| `POST /auth/prelogin` | `username` → `kdfSalt, iterations` (unknown usernames get a stable made-up salt) |
| `POST /auth/register` | `username, inviteCode?, authSecret, kdfSalt, iterations, wrappedKey, recoveryAuthSecret, recoveryWrappedKey, device` → session |
| `POST /auth/login` | `username, authSecret, device` → session |
| `POST /auth/recover/start` | `username, recoveryAuthSecret` → `recoveryWrappedKey` |
| `POST /auth/recover/finish` | `username, recoveryAuthSecret, newAuthSecret, kdfSalt, iterations, wrappedKey, device` → session (other devices are signed out) |
| `POST /auth/password` | `currentAuthSecret, newAuthSecret, kdfSalt, iterations, wrappedKey` (other devices are signed out) |
| `POST /auth/logout` | signs this device out |
| `GET /account` | → `username, createdAt, records, fileBytes, devices[id, name, platform, appVersion, lastSeen, isThisDevice]` |
| `DELETE /account/devices/{id}` | signs that device out |
| `GET /sync/changes?since=N` | → `changes[collection, id, seq, modified, deleted, payload?], latest, more` (up to 1,000, oldest first) |
| `POST /sync/push` | `changes[collection, id, modified, deleted, payload?]` (up to 500) → `results[collection, id, status, seq], latest` |
| `PUT /files/{name}` | sealed bytes (up to 50 MB) |
| `GET`/`HEAD /files/{name}` | → sealed bytes, or 404 |
| `DELETE /files/{name}` | |

A session is `token, username, wrappedKey, kdfSalt, iterations, latestSeq`. `device` is `name, platform, appVersion`.
Ten failed sign-ins (or recoveries) for a name or address in 15 minutes lock it for 15 minutes.

## Syncing

- Each account has a counter. Every accepted change gets the next number (`seq`); records keep their latest.
- `modified` is the record's last edit time in Unix milliseconds, set by the device that made the edit.
- **Push.** A change is accepted unless the server's copy has a later `modified`; then it's `stale` and the result
  carries the server copy's `seq`. A device that gets `stale` lowers its "last seen" number below that `seq` so its
  next pull fetches the newer copy.
- **Pull.** Devices ask for everything after the last `seq` they saw, page by page while `more` is true, and
  apply each record unless they've changed it since and their edit is newer (it is sent next time).
- **Deletes** are pushed with `deleted: true` and no payload, stamped later than the last version the device had,
  and kept on the server so devices that were away still see them.
- A device remembers, for each record, the edit time the server last had. A record whose edit time differs, or that
  has disappeared, is a local change to send. Records arriving from the server are recorded the same way, so they
  aren't sent back.

### Collections

| Collection | What | Windows | Android |
| --- | --- | --- | --- |
| `transactions`, `budgets` | as in the Windows app's data files (PascalCase JSON) | edits | edits, keeping fields it doesn't show (splits, tags, receipt, transfer…) |
| `accounts`, `categories` | | edits | reads (account names, custom categories) |
| `debts`, `bills`, `goals`, `rules`, `tax-documents` | | edits | leaves on the server |
| `settings` / `preferences`, `debt-plan` | settings files | edits | leaves on the server |
| files `receipt/<name>`, `tax/<name>` | receipts and tax documents | uploads and downloads | — |

Collection names are lowercase letters, digits and dashes (up to 40 characters); ids and file names are letters,
digits, `.`, `-`, `_` and `/` (up to 200, no `..`). Clients ignore collections they don't know, so newer versions can add more.
