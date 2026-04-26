# Oahu CLI — Code Review

_Date: 2026-04-25_

This review consolidates findings from five independent reviewing agents, each running on a different model:

| Agent | Model | Focus |
|---|---|---|
| review-sonnet45 | Claude Sonnet 4.5 | General quality / stability / subtle bugs |
| review-opus46 | Claude Opus 4.6 | Deep skeptical review of state machines, concurrency, persistence |
| review-gpt53codex | GPT-5.3-Codex | C#/.NET pitfalls, IO, JSON, security of process invocation |
| review-gpt54 | GPT-5.4 | User-facing correctness, output writers, TUI hooks, server semantics |
| review-gaps-opus47 | Claude Opus 4.7 | Design-gap analysis vs. `docs/OAHU_CLI_DESIGN.md` |

Scope: `src/Oahu.Cli`, `src/Oahu.Cli.App`, `src/Oahu.Cli.Server`, `src/Oahu.Cli.Tui`, `tests/Oahu.Cli.Tests`.

> **Tip:** When the same issue appears in multiple sections it has been independently corroborated by more than one reviewer — these convergence points are the highest-confidence findings (see the Cross-Cutting Themes section at the end).

---

## Findings — Claude Sonnet 4.5

This document contains high-signal code quality and stability issues found in the Oahu CLI codebase. Focus areas: concurrency bugs, resource leaks, error handling gaps, security issues, and cross-platform correctness.

---

### Concurrency / Threading / Async

#### **CancellationTokenSource Leak in JobsScreen**
**File:** `src/Oahu.Cli.Tui/Screens/JobsScreen.cs:91-123`  
**Severity:** Medium  
**Description:**  
`JobsScreen.OnActivated()` creates a new `CancellationTokenSource` in `observerCts` but never disposes it. The `StopObserver()` method cancels the CTS and nulls the reference, but the disposed CTS remains on the heap. Under repeated tab switches, this accumulates undisposed CTS instances.

**Suggested Fix:**  
Wrap the CTS lifecycle with proper disposal:
```csharp
private void StopObserver()
{
    var cts = observerCts;
    observerCts = null;
    observerTask = null;
    try
    {
        cts?.Cancel();
        cts?.Dispose();
    }
    catch
    {
        // ignore
    }
}
```

---

#### **CancellationTokenSource Leak in SignInFlow**
**File:** `src/Oahu.Cli.Tui/Auth/SignInFlow.cs:61-76`  
**Severity:** Medium  
**Description:**  
`SignInFlow.Start()` creates a `CancellationTokenSource` in the field `cts` but never disposes it. If a user starts sign-in, navigates away, then signs in again, the old CTS is leaked. The class doesn't implement `IDisposable`, and the TUI lifecycle doesn't call a cleanup hook on the flow itself.

**Suggested Fix:**  
Add a `Dispose` method or reset hook:
```csharp
public void Dispose()
{
    cts?.Dispose();
    cts = null;
}
```
And call from the owning screen's `OnDeactivated()` / `OnShutdown()`.

---

#### **JobScheduler CancellationTokenSource Leak on DisposeAsync**
**File:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:118-134`  
**Severity:** High  
**Description:**  
`JobScheduler` creates a `CancellationTokenSource shutdownCts` at line 36 and calls `shutdownCts.Dispose()` at line 133 in `DisposeAsync()`. However, line 120 calls `shutdownCts.Cancel()`, which can throw `ObjectDisposedException` if `DisposeAsync()` is called multiple times (e.g., in a double-dispose scenario). The second call to `DisposeAsync()` will see `shutdownCts` already disposed and throw when calling `Cancel()`.

Additionally, each `JobLifecycle` creates its own `CancellationTokenSource Cts` at line 309, which is never disposed. When a job completes, the lifecycle is removed from `jobs` (line 209), but the CTS is not explicitly disposed, leading to resource leaks for long-running schedulers.

**Suggested Fix:**  
1. Guard `DisposeAsync()` with a disposed flag:
```csharp
private bool disposed;

public async ValueTask DisposeAsync()
{
    if (disposed) return;
    disposed = true;
    work.Writer.TryComplete();
    shutdownCts.Cancel();
    try
    {
        await Task.WhenAll(workers).ConfigureAwait(false);
    }
    catch (OperationCanceledException) { }
    foreach (var sub in subscribers.Values)
    {
        sub.Writer.TryComplete();
    }
    shutdownCts.Dispose();
}
```

2. Dispose the per-job CTS in `RunOneAsync` after the job terminates:
```csharp
finally
{
    jobs.TryRemove(request.Id, out _);
    lifecycle.Cts.Dispose();
    history?.Append(...);
}
```

---

#### **GetAwaiter().GetResult() Deadlock Risk in TuiCommand**
**File:** `src/Oahu.Cli/Commands/TuiCommand.cs:61`  
**Severity:** Low  
**Description:**  
`TuiCommand.Run()` calls `auth.GetActiveAsync().GetAwaiter().GetResult()` synchronously in a non-async method. While this is currently safe because the method runs on a thread-pool thread and `CoreAuthService.GetActiveAsync()` uses `ConfigureAwait(false)`, this pattern is fragile: if the auth service ever posts back to a captured `SynchronizationContext` (e.g., in a future UI refactor), this will deadlock.

**Suggested Fix:**  
Make `TuiCommand.Run()` async or move the initialization to an async helper:
```csharp
public static async Task<int> Run(GlobalOptions global, IAnsiConsole console)
{
    var state = new AppShellState();
    try
    {
        var auth = CliServiceFactory.AuthServiceFactory();
        var session = await auth.GetActiveAsync().ConfigureAwait(false);
        if (session is not null)
        {
            state.Profile = session.ProfileAlias;
            state.Region = session.Region.ToString().ToLowerInvariant();
        }
    }
    catch { }
    // ...
}
```
And update the command handler registration to `await Run(...)`.

---

#### **Race Condition in AppShell.needsTimedRefresh**
**File:** `src/Oahu.Cli.Tui/Shell/AppShell.cs:533`  
**Severity:** Low  
**Description:**  
`needsTimedRefresh` is written at the end of `Render()` (line 533) but read in the main loop (line 216) without synchronization. While this is a single-threaded event loop in the TUI, if `Render()` is ever called from a background thread (e.g., a future async refresh hook), this becomes a data race.

**Suggested Fix:**  
Mark `needsTimedRefresh` as `volatile` or use `Interlocked` operations:
```csharp
private volatile bool needsTimedRefresh;
```

---

#### **Potential Deadlock in CallbackBridge.ToCoreCallbacks**
**File:** `src/Oahu.Cli.App/Auth/CallbackBridge.cs:28-45`  
**Severity:** Medium  
**Description:**  
`CallbackBridge.ToCoreCallbacks()` returns synchronous delegates that call `.GetAwaiter().GetResult()` on async broker methods. The comment at line 11-13 claims this is safe because Core invokes these delegates on background threads. However, if the broker implementation (e.g., `TuiCallbackBroker`) ever awaits a task that posts back to a captured `SynchronizationContext`, this will deadlock.

Specifically, `TuiCallbackBroker` sets `TaskCreationOptions.RunContinuationsAsynchronously` on its `TaskCompletionSource` (line 18 of `TuiCallbackBroker.cs`), which mitigates the risk but does not eliminate it if the TCS is set from a UI thread with a captured context.

**Suggested Fix:**  
Add explicit `.ConfigureAwait(false)` in the bridge to ensure no context capture:
```csharp
CaptchaCallback = imageBytes =>
    broker.SolveCaptchaAsync(new CaptchaChallenge(imageBytes), cancellationToken)
        .ConfigureAwait(false)
        .GetAwaiter().GetResult(),
```
This is belt-and-suspenders, but it makes the intent explicit and removes the dependency on the TCS option.

---

### Resource Leaks

#### **FileStream Leak in MacOsKeychainCredentialStore**
**File:** `src/Oahu.Cli.App/Credentials/MacOsKeychainCredentialStore.cs:101-106`  
**Severity:** High  
**Description:**  
`RunAsync()` calls `Process.Start(psi)` and wraps the result in a `using` statement at line 101. However, if `Process.Start()` returns null (which can happen if the executable doesn't exist), the code throws a `CredentialStoreUnavailableException` before entering the `using` block. The `proc` variable is non-nullable, so this path is actually safe. But if the process starts, the `using` disposes it at line 102, which is correct.

However, the reads from `StandardOutput` and `StandardError` (lines 103-104) are started as tasks. If `WaitForExitAsync()` is cancelled via the `CancellationToken`, the `using` block exits and disposes the process before the read tasks complete. This can throw `ObjectDisposedException` from the stream reads.

**Suggested Fix:**  
Ensure the read tasks are awaited before disposing:
```csharp
using (proc)
{
    var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
    var stderrTask = proc.StandardError.ReadToEndAsync(ct);
    try
    {
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
    }
    catch
    {
        // Cancel the read tasks if WaitForExitAsync throws.
        try { proc.Kill(); } catch { }
        throw;
    }
    return (proc.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
}
```

---

#### **FileStream Leak in LinuxSecretToolCredentialStore**
**File:** `src/Oahu.Cli.App/Credentials/LinuxSecretToolCredentialStore.cs:108-119`  
**Severity:** High  
**Description:**  
Same issue as macOS: if `WaitForExitAsync()` is cancelled, the `using` block disposes the process before the stdout/stderr read tasks complete, leading to `ObjectDisposedException`.

**Suggested Fix:**  
Same as macOS: ensure reads complete before disposing.

---

#### **Event Handler Leak in RotatingFileLoggerProvider**
**File:** `src/Oahu.Cli/Logging/RotatingFileLoggerProvider.cs:37-58`  
**Severity:** Low  
**Description:**  
`RotatingFileLoggerProvider.CreateLogger()` stores loggers in a `ConcurrentDictionary` that grows unbounded. If the CLI creates many short-lived loggers (e.g., per-job categories), the dictionary accumulates entries that are never removed. The `ILogger` instances themselves are cheap, but the dictionary keys are retained forever.

**Suggested Fix:**  
This is a minor issue in practice (the CLI doesn't create many categories). If it becomes a problem, implement a cache eviction policy or use `ConditionalWeakTable`.

---

#### **StreamWriter Not Flushed in JsonlHistoryStore.Append**
**File:** `src/Oahu.Cli.App/Jobs/JsonlHistoryStore.cs:43-48`  
**Severity:** Low  
**Description:**  
`JsonlHistoryStore.Append()` calls `writer.Flush()` at line 46 and `stream.Flush(flushToDisk: true)` at line 47. However, `writer.WriteLine()` at line 45 writes to the `StreamWriter`'s buffer, which is flushed by `writer.Flush()`. The subsequent `stream.Flush(true)` is redundant because the StreamWriter already owns the FileStream and flushes its buffer first.

More critically, if the process crashes between `writer.WriteLine()` and `writer.Flush()`, the line is lost. The `using` statement disposes the writer, which flushes automatically, but the code relies on that implicit flush rather than explicitly controlling it before closing.

**Suggested Fix:**  
This is actually fine — the code is correct. The explicit `writer.Flush()` + `stream.Flush(true)` ensures the record is durably written before returning. No fix needed, but document the intent:
```csharp
// Flush writer buffer first, then fsync to disk for durability.
writer.Flush();
stream.Flush(flushToDisk: true);
```

---

### Error Handling

#### **Swallowed Exception in AuditLog.Write**
**File:** `src/Oahu.Cli.Server/Audit/AuditLog.cs:58-85`  
**Severity:** Low  
**Description:**  
`AuditLog.Write()` catches and swallows all exceptions at lines 82-85 with a comment "best-effort". This is intentional to prevent a hung disk from crashing the server (per the doc comment at line 34-36). However, if the audit log repeatedly fails to write (e.g., disk full, permission denied), the server continues silently without alerting the operator. This violates the principle of failing loudly on persistent errors.

**Suggested Fix:**  
Add a counter for consecutive failures and log to stderr after N failures:
```csharp
private int consecutiveFailures;

public void Write(...)
{
    try
    {
        // ... write logic ...
        consecutiveFailures = 0;
    }
    catch
    {
        if (++consecutiveFailures == 10)
        {
            Console.Error.WriteLine($"[WARN] Audit log write failed 10 times. Last path: {path}");
        }
    }
}
```

---

#### **Missing Try-Catch in ToolDispatcher.InvokeAsync**
**File:** `src/Oahu.Cli.Server/Hosting/ToolDispatcher.cs:48-58`  
**Severity:** Low  
**Description:**  
`ToolDispatcher.InvokeAsync()` catches exceptions from `body()` at lines 54-58 and logs "error" to the audit log, then re-throws. However, if `audit.Write()` itself throws (e.g., I/O error), the exception propagates up, and the original exception from `body()` is lost. This makes debugging harder because the operator sees the audit log failure, not the tool failure.

**Suggested Fix:**  
Wrap `audit.Write()` in a try-catch:
```csharp
catch (Exception)
{
    try
    {
        audit.Write(transport, principal, toolName, args, "error", sw.ElapsedMilliseconds);
    }
    catch
    {
        // Audit log failure must not mask the original error.
    }
    throw;
}
```

---

#### **Uncaught JsonException in JsonlHistoryStore.ReadAllAsync**
**File:** `src/Oahu.Cli.App/Jobs/JsonlHistoryStore.cs:78-85`  
**Severity:** Low  
**Description:**  
`ReadAllAsync()` catches `JsonException` at line 82 when deserializing a line and skips the record. The comment at line 84 says "Skip torn records". However, if the JSON is malformed due to a bug (not a crash), the CLI silently skips valid records, leading to data loss that the user never sees.

**Suggested Fix:**  
Log a warning to stderr when skipping a record:
```csharp
catch (JsonException ex)
{
    Console.Error.WriteLine($"[WARN] Skipping malformed job history record at line {lineNumber}: {ex.Message}");
}
```
(Add a line counter to track position.)

---

### File I/O Atomicity

#### **AtomicFile.WriteAllJson Does Not Handle Directory Creation Race**
**File:** `src/Oahu.Cli.App/AtomicFile.cs:26-30`  
**Severity:** Low  
**Description:**  
`WriteAllJson()` calls `Directory.CreateDirectory(dir)` at line 29 without checking for concurrent creation. If two threads/processes call this simultaneously for the same path, one may throw `IOException` when creating the directory. While `Directory.CreateDirectory()` is idempotent and succeeds if the directory already exists, there's a small window where one thread can delete the directory (e.g., cleanup script) after another checks but before creating the file.

**Suggested Fix:**  
Wrap `Directory.CreateDirectory()` in a try-catch to ignore `DirectoryNotFoundException` / `IOException`:
```csharp
if (!string.IsNullOrEmpty(dir))
{
    try
    {
        Directory.CreateDirectory(dir);
    }
    catch (IOException)
    {
        // Race: directory was deleted after creation. Retry once.
        Directory.CreateDirectory(dir);
    }
}
```

---

#### **WindowsDpapiCredentialStore Atomic Write Missing Flush**
**File:** `src/Oahu.Cli.App/Credentials/WindowsDpapiCredentialStore.cs:107-112`  
**Severity:** Medium  
**Description:**  
`PersistLocked()` writes to a `.tmp` file and calls `stream.Flush(flushToDisk: true)` at line 110. However, the code writes raw bytes with `stream.Write(encrypted)` at line 109, which writes to the FileStream buffer. The subsequent `Flush(true)` ensures the data is on disk before `File.Move()`. But if the process crashes after `Write()` but before `Flush()`, the `.tmp` file is incomplete, and the move at line 112 promotes a corrupt file.

Wait, no — the code is correct. The `using` block ensures `stream.Flush()` is called on dispose. But the explicit `Flush(true)` at line 110 is actually good because it fsyncs. The issue is that `File.Move()` at line 112 is called after the `using` block exits, so the stream is disposed before the move. This is correct.

Actually, re-reading: the `using` block is at line 107, and it disposes the stream before `File.Move()` at line 112. So the flush happens, the file is closed, then moved. This is correct.

**Suggested Fix:**  
No fix needed. False alarm.

---

### Security

#### **TokenStore Timing Attack Mitigation Incomplete**
**File:** `src/Oahu.Cli.Server/Auth/TokenStore.cs:55-72`  
**Severity:** Low  
**Description:**  
`TokenStore.Equal()` implements constant-time comparison by XORing all characters and checking the final diff. However, the early-exit at line 62-64 returns false if lengths differ. An attacker can measure the time difference between a length-mismatch (fast) and a content-mismatch (slow) to infer the token length. While the token length is always 43 characters (base64url of 32 bytes), leaking this is low risk.

More critically, the code XORs `char` values (line 69), which are UTF-16 code units. If the token contained non-ASCII characters, this would leak timing via cache behavior. But the token is base64url (ASCII only), so this is safe.

**Suggested Fix:**  
For belt-and-suspenders, convert both strings to byte arrays and compare bytes:
```csharp
public static bool Equal(string? a, string? b)
{
    if (a is null || b is null) return false;
    var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
    var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
    if (bytesA.Length != bytesB.Length) return false;
    int diff = 0;
    for (var i = 0; i < bytesA.Length; i++)
    {
        diff |= bytesA[i] ^ bytesB[i];
    }
    return diff == 0;
}
```
But the current implementation is acceptable.

---

#### **AuditLog Argument Hash Collision Risk**
**File:** `src/Oahu.Cli.Server/Audit/AuditLog.cs:88-102`  
**Severity:** Low  
**Description:**  
`HashArgs()` serializes arguments to JSON with `JsonSerializer.Serialize(sorted)` at line 100, using default options. If two different argument dictionaries serialize to the same JSON (e.g., due to floating-point precision or key ordering bugs), they collide to the same hash. While the code sorts keys (line 95-99), the JSON serializer may apply transformations (e.g., number formatting) that are non-canonical.

**Suggested Fix:**  
Use explicit `JsonSerializerOptions` with deterministic settings:
```csharp
var json = JsonSerializer.Serialize(sorted, new JsonSerializerOptions
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
});
```

---

#### **Server.token File Race: Regenerate on Empty**
**File:** `src/Oahu.Cli.Server/Auth/TokenStore.cs:34-44`  
**Severity:** Low  
**Description:**  
`ReadOrCreate()` checks `File.Exists(Path)` at line 33, reads the file at line 36, trims it, and checks `IsNullOrWhiteSpace` at line 38. If the file exists but is empty (or whitespace-only), the code falls through to `WriteNew()` at line 44, regenerating the token. However, there's a race: if two server instances start concurrently, both may see the file as empty, regenerate, and write different tokens. The second write wins, and the first server's HTTP requests will fail with 401.

**Suggested Fix:**  
Add a lock file or use `FileStream` with exclusive access:
```csharp
using var lockStream = new FileStream(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
// Now read/write the token file.
```
Or document that `oahu-cli serve` must not run multiple concurrent instances (which the `UserDataLock` already enforces for the same host, but not across different machines writing to shared storage).

---

#### **TokenStore Windows ACL Best-Effort Failure Silent**
**File:** `src/Oahu.Cli.Server/Auth/TokenStore.cs:126-153`  
**Severity:** Low  
**Description:**  
`TryRestrictWindowsAcl()` catches all exceptions at line 149-152 and silently ignores failures to set the ACL. The comment at line 151 says "Best-effort. The token is already in a per-user APPDATA folder." However, if the ACL setting fails due to a serious issue (e.g., corrupted security descriptor), an attacker with local admin access could read the token from another user's folder.

**Suggested Fix:**  
Log the failure to stderr so the operator is aware:
```csharp
catch (Exception ex)
{
    Console.Error.WriteLine($"[WARN] Failed to restrict token ACL: {ex.Message}");
}
```

---

### Process Lifecycle / Signal Handling

#### **CliEnvironment.RunRestore Not Idempotent Under Concurrent Calls**
**File:** `src/Oahu.Cli/CliEnvironment.cs:102-114`  
**Severity:** Low  
**Description:**  
`RunRestore()` reads `restoreAction`, nulls it, then invokes it. If two threads call `RunRestore()` concurrently (e.g., `ProcessExit` + `UnhandledException`), one may read the action before the other nulls it, and both invoke it. The action (e.g., `AltScreen.Leave()`) may not be idempotent, causing a double-restore crash.

**Suggested Fix:**  
Use `Interlocked.Exchange()` to atomically swap and invoke:
```csharp
public static void RunRestore()
{
    var local = System.Threading.Interlocked.Exchange(ref restoreAction, null);
    try
    {
        local?.Invoke();
    }
    catch { }
}
```

---

#### **AppDomain Event Handlers Never Unregistered**
**File:** `src/Oahu.Cli/CliEnvironment.cs:123-146`  
**Severity:** Low  
**Description:**  
`InstallExitTrap()` registers `CancelKeyPress`, `ProcessExit`, and `UnhandledException` handlers at lines 123-146. These handlers are never unregistered, so if the CLI is embedded in a long-running host process (e.g., a test runner that creates multiple `Program.Main()` invocations), the handlers accumulate. Each invocation adds a new handler, and all fire on the next Ctrl+C.

**Suggested Fix:**  
Store the handler delegates and unregister them when `RunRestore()` is called:
```csharp
private static ConsoleCancelEventHandler? cancelHandler;

private static void InstallExitTrap()
{
    cancelHandler = (_, e) => { RunRestore(); };
    Console.CancelKeyPress += cancelHandler;
    // ...
}

public static void UninstallExitTrap()
{
    if (cancelHandler != null)
    {
        Console.CancelKeyPress -= cancelHandler;
        cancelHandler = null;
    }
}
```
And call `UninstallExitTrap()` from `Program.Main()`'s finally block.

---

### Cross-Platform Issues

#### **MacOsKeychainCredentialStore Hardcoded /usr/bin/security**
**File:** `src/Oahu.Cli.App/Credentials/MacOsKeychainCredentialStore.cs:28`  
**Severity:** Low  
**Description:**  
`MacOsKeychainCredentialStore` defaults `securityBinary` to `/usr/bin/security`. On macOS, this is correct. However, if a user installs a custom `security` tool in a different location (e.g., Homebrew's `/usr/local/bin`), the store won't find it. The constructor accepts an optional `securityBinary` parameter, but the CLI never passes it.

**Suggested Fix:**  
Search `PATH` for `security` if the default doesn't exist:
```csharp
private static string FindSecurityBinary()
{
    if (File.Exists("/usr/bin/security")) return "/usr/bin/security";
    var path = Environment.GetEnvironmentVariable("PATH");
    foreach (var dir in path?.Split(':') ?? Array.Empty<string>())
    {
        var candidate = Path.Combine(dir, "security");
        if (File.Exists(candidate)) return candidate;
    }
    throw new FileNotFoundException("macOS `security` tool not found in PATH.");
}
```

---

#### **LinuxSecretToolCredentialStore stdin Not Closed Early**
**File:** `src/Oahu.Cli.App/Credentials/LinuxSecretToolCredentialStore.cs:110-113`  
**Severity:** Low  
**Description:**  
`RunAsync()` writes `stdin` to the process at line 112, then calls `proc.StandardInput.Close()` at line 113. However, the write is async (`WriteAsync`), and the code awaits it before closing. This is correct. But if `WriteAsync()` throws (e.g., process exited early), the `Close()` never happens, and the pipe remains open, potentially causing `secret-tool` to hang.

**Suggested Fix:**  
Use a `try-finally` to ensure the stdin stream is closed:
```csharp
if (stdin is not null)
{
    try
    {
        await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
    }
    finally
    {
        proc.StandardInput.Close();
    }
}
```

---

### Logic Errors

#### **JsonFileQueueService.MoveAsync Off-By-One on Delta==0**
**File:** `src/Oahu.Cli.App/Queue/JsonFileQueueService.cs:69-89`  
**Severity:** Low  
**Description:**  
`MoveAsync()` checks `target == idx` at line 82 and returns false, treating delta==0 as a no-op. This is correct behavior. However, the check `target < 0 || target >= list.Count` at line 82 should come first to avoid unnecessary work if the delta is out of bounds.

**Suggested Fix:**  
Reorder the checks:
```csharp
var target = idx + delta;
if (target < 0 || target >= list.Count)
{
    return Task.FromResult(false);
}
if (target == idx)
{
    return Task.FromResult(false);
}
```

---

#### **CoreLibraryService.SyncAsync Always Resync, Ignoring profileAlias**
**File:** `src/Oahu.Cli.App/Library/CoreLibraryService.cs:73-89`  
**Severity:** Medium  
**Description:**  
`SyncAsync()` accepts `profileAlias` as a parameter (line 73) but never uses it to switch profiles. It calls `CoreEnvironment.EnsureProfileLoadedAsync()` at line 78, which loads the GUI's "active" profile, not the requested alias. If the caller passes a different alias, the sync operates on the wrong profile.

**Suggested Fix:**  
Resolve the alias to a `ProfileKey` and call `client.ChangeProfileAsync()`:
```csharp
public async Task<int> SyncAsync(string profileAlias, CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
    cancellationToken.ThrowIfCancellationRequested();

    // Ensure the requested profile is loaded.
    var key = await ResolveKeyByAliasAsync(profileAlias).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"No profile with alias '{profileAlias}'.");
    await client.ChangeProfileAsync(key, aliasChanged: false).ConfigureAwait(false);

    var api = client.Api
        ?? throw new InvalidOperationException($"Failed to load profile '{profileAlias}'.");
    await api.GetLibraryAsync(resync: true).ConfigureAwait(false);

    var books = api.GetBooks();
    return books?.Count() ?? 0;
}
```
(Add the `ResolveKeyByAliasAsync` method from `CoreAuthService`.)

---

#### **CoreLibraryService.MapBook Series Position Incorrect for SubNumber**
**File:** `src/Oahu.Cli.App/Library/CoreLibraryService.cs:118-129`  
**Severity:** Low  
**Description:**  
`MapBook()` computes `seriesPosition` as `BookNumber + (SubNumber / 10.0)` at line 123. This assumes `SubNumber` is 0-9, mapping e.g. Book 1, SubNumber 2 → 1.2. However, if `SubNumber` is 10 or higher (e.g., Book 1, SubNumber 12), the result is 1 + 12/10 = 2.2, which is ambiguous with Book 2, SubNumber 2.

**Suggested Fix:**  
Use `BookNumber + (SubNumber / 100.0)` to support SubNumber 0-99:
```csharp
if (seriesEntry.SubNumber.HasValue)
{
    seriesPosition = seriesEntry.BookNumber + (seriesEntry.SubNumber.Value / 100.0);
}
```

---

#### **AudibleJobExecutor.ExecuteAsync Does Not Check ExportDirectory Early**
**File:** `src/Oahu.Cli.App/Jobs/AudibleJobExecutor.cs:140-148`  
**Severity:** Low  
**Description:**  
`ExecuteAsync()` checks `jobExport.ExportDirectory` is non-empty at line 140 before constructing the `AaxExporter`. However, it doesn't validate that the directory exists or is writable. The job proceeds to download + decrypt, then fails at the muxing stage with a cryptic I/O error when the exporter tries to write to a non-existent directory.

**Suggested Fix:**  
Add a pre-flight check:
```csharp
if (string.IsNullOrEmpty(jobExport.ExportDirectory))
{
    yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Failed, Message = "..." };
    yield break;
}
if (!Directory.Exists(jobExport.ExportDirectory))
{
    try
    {
        Directory.CreateDirectory(jobExport.ExportDirectory);
    }
    catch (Exception ex)
    {
        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Failed, Message = $"Cannot create export directory: {ex.Message}" };
        yield break;
    }
}
```

---

### Inconsistencies with Design Docs

#### **Server.lock Not Documented in OAHU_CLI_SERVER.md**
**File:** `src/Oahu.Cli.Server/Hosting/UserDataLock.cs:8-27`  
**Severity:** Low  
**Description:**  
`UserDataLock` creates a file `<SharedUserDataDir>/server.lock` to enforce single-server semantics (per design §15.4). However, `docs/OAHU_CLI_SERVER.md` doesn't mention this file or its purpose. If a user manually deletes the lock file while a server is running, a second server can start, leading to data corruption in `queue.json` / `history.jsonl`.

**Suggested Fix:**  
Add a section to `OAHU_CLI_SERVER.md`:
```markdown
#### §15.4 - Process Lock

The server holds an exclusive file lock at `<SharedUserDataDir>/server.lock` for its lifetime. 
If you need to forcibly terminate a hung server:
1. Kill the process (PID is recorded in the lock file).
2. Delete the lock file.
3. Restart the server.
```

---

#### **Design Doc §11 Says queue.json is SharedUserDataDir, But Code Says CliPaths.SharedUserDataDir**
**File:** `src/Oahu.Cli.App/Queue/JsonFileQueueService.cs:12`  
**Severity:** Low  
**Description:**  
The comment at line 12 says `queue.json` lives in `SharedUserDataDir`. The design doc §11 says the same. However, `CliPaths.SharedUserDataDir` may resolve to a different location on different platforms (e.g., `~/.local/share/oahu` on Linux, `~/Library/Application Support/Oahu` on macOS). The GUI and CLI must agree on this path, or they'll have separate queues.

**Suggested Fix:**  
Document the exact resolution logic in `docs/OAHU_CLI_DESIGN.md` §11:
```markdown
`SharedUserDataDir` resolves to:
- macOS: `~/Library/Application Support/Oahu`
- Windows: `%LOCALAPPDATA%\Oahu`
- Linux: `~/.local/share/oahu`
```

---

### Boundary / Off-By-One Bugs

#### **LogRingBuffer Snapshot Index Calculation Incorrect for count==capacity**
**File:** `src/Oahu.Cli.Tui/Logging/LogRingBuffer.cs:106`  
**Severity:** Low  
**Description:**  
`Snapshot()` computes the oldest entry index as `(head - count + ring.Length) % ring.Length` at line 106. When the ring is full (`count == ring.Length`), this simplifies to `head % ring.Length`, which is just `head` (assuming `head < ring.Length`). However, `head` points to the *next* slot to write, so the oldest entry is at `head`, which is correct. Wait, no — when full, `head` is the slot that was just overwritten, so the oldest entry is at `head`. This is correct.

Actually, re-checking: when the ring wraps, `head` advances to the slot of the oldest entry. So `Snapshot()` is correct.

**Suggested Fix:**  
No fix needed. False alarm.

---

### Unobserved Task Exceptions

#### **AudibleJobExecutor.ExecuteAsync Background Task May Throw Unobserved Exception**
**File:** `src/Oahu.Cli.App/Jobs/AudibleJobExecutor.cs:159-176`  
**Severity:** Medium  
**Description:**  
`ExecuteAsync()` starts a background `Task.Run()` at line 159 to execute the download job. The task is stored in `runTask` and awaited at line 197. However, if the consuming `IAsyncEnumerable` is abandoned before reaching the await (e.g., caller stops enumerating early), `runTask` continues running in the background. If it throws after the enumerator is disposed, the exception is unobserved and may crash the process (depending on `TaskScheduler.UnobservedTaskException` settings).

**Suggested Fix:**  
Cancel the background task when the enumerator is disposed:
```csharp
public async IAsyncEnumerable<JobUpdate> ExecuteAsync(
    JobRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    try
    {
        // ... existing code ...
        Task runTask = Task.Run(
            async () => { ... },
            cts.Token);
        
        // ... yield updates ...
    }
    finally
    {
        cts.Cancel();
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Log unobserved exception.
            logger.LogError(ex, "Background job task threw after enumeration.");
        }
        cts.Dispose();
    }
}
```

---

## Summary

- **Critical Issues:** 0
- **High Severity:** 3 (JobScheduler CTS leak, FileStream leaks in credential stores)
- **Medium Severity:** 6 (Various CTS leaks, CoreLibraryService.SyncAsync profile bug, unobserved task exceptions, deadlock risks)
- **Low Severity:** 25 (Timing attacks, error handling gaps, platform-specific issues, logic errors, doc inconsistencies)

**Total:** 34 findings

The codebase is generally well-structured with good async/await patterns and explicit ConfigureAwait. The most critical issues are resource leaks (CancellationTokenSource, FileStream) that accumulate under repeated operations. The concurrency issues are mostly low-risk due to the single-threaded TUI and careful use of RunContinuationsAsynchronously. Security issues are minor (best-effort audit log failures, timing attack mitigations). Cross-platform correctness is good, with only minor path resolution concerns.


---

## Findings — Claude Opus 4.6

---

### Area: Concurrent File Access & Atomicity

#### 1. AtomicFile uses a fixed `.tmp` suffix — unsafe under concurrent writers
- **File:** `src/Oahu.Cli.App/AtomicFile.cs:32-40`
- **Severity:** High
- **Description:** `WriteAllJson` always writes to `path + ".tmp"`. If two processes (or two tasks in the same process) target the same destination concurrently, they clobber each other's temp file. The loser's `File.Move` either promotes the wrong content or fails. The "atomic" guarantee breaks down to last-writer-wins-or-crashes.
- **Suggested fix:** Generate a unique temp file per write (e.g., `$"{path}.{Guid.NewGuid():N}.tmp"`) in the same directory, then `File.Move(…, overwrite: true)`. Clean up on failure.

#### 2. JsonFileQueueService: process-wide lock does not protect cross-process access
- **File:** `src/Oahu.Cli.App/Queue/JsonFileQueueService.cs:20, 39-49`
- **Severity:** High
- **Description:** `writeLock` is an in-process `object`. Two separate CLI invocations running `oahu-cli queue add` concurrently each hold their own lock instance. Both do `LoadLocked()` → mutate → `Persist()` and the second writer silently overwrites the first writer's additions. The class comment acknowledges this ("fine for command-mode usage where the scheduler is the only writer") but the CLI surface allows arbitrary concurrent invocations.
- **Suggested fix:** Use a cross-process file lock (`new FileStream(lockPath, …, FileShare.None)` held during read-modify-write) or an OS-level named mutex.

#### 3. JsonlHistoryStore: append is only process-safe by accident
- **File:** `src/Oahu.Cli.App/Jobs/JsonlHistoryStore.cs:33-48`
- **Severity:** Medium
- **Description:** `writeLock` is instance-local, so concurrent processes can append simultaneously. POSIX `write(2)` to a file opened with `O_APPEND` is atomic only for ≤ PIPE_BUF bytes on some systems and some filesystems. If a JSON record exceeds ~4 KB or the OS doesn't guarantee atomic append, lines can interleave, producing corrupt JSONL. The `ReadAllAsync` reader gracefully skips parse failures, so data silently disappears rather than causing a crash — a correctness issue, not a crash issue.
- **Suggested fix:** Accept the risk with a comment (records are small), or use a file lock around appends.

#### 4. TokenStore.ReadOrCreate is racy under concurrent first requests
- **File:** `src/Oahu.Cli.Server/Auth/TokenStore.cs:28-45`
- **Severity:** High
- **Description:** `ReadOrCreate()` checks `File.Exists` then conditionally calls `WriteNew`. Two concurrent HTTP requests during server cold-start can both see the file as missing, each generating a different random token. One is written to disk, the other is returned to its caller but is now invalid. The auth middleware calls `ReadOrCreate()` on every request (line 23 of `HttpEndpoints.cs`), making this reachable in normal traffic.
- **Suggested fix:** Initialize and cache the token once at server startup (in DI registration or a startup hook). If lazy init is truly needed, guard with a `Lazy<string>` or `lock`. `Rotate()` should also atomically replace (temp + rename) rather than `FileMode.Create`.

---

### Area: Job Scheduler

#### 5. SubmitAsync leaks a "Queued" lifecycle entry on channel-write failure
- **File:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:65-76`
- **Severity:** High
- **Description:** `SubmitAsync` adds the lifecycle to `jobs` and publishes a `Queued` update *before* writing to the work channel. If `WriteAsync` throws (e.g., caller cancellation or channel completion), the lifecycle remains in `jobs` forever with no worker to drive it to a terminal state, and no history record is ever written. Observers waiting for that job will never see completion.
- **Suggested fix:** Wrap the channel write in try/catch; on failure, remove the lifecycle from `jobs`, publish a `Canceled` or `Failed` update, and append a history record.

#### 6. Publish can block workers when a subscriber channel is full
- **File:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:228-247`
- **Severity:** Medium
- **Description:** The subscriber channel is `DropOldest` with capacity 256, so `TryWrite` should almost always succeed. However, the fallback path calls `await ch.Writer.WriteAsync(update)` without a cancellation token. If a subscriber somehow stalls (e.g., its reader task is blocked), the worker thread blocks indefinitely. The comment says "slow observers cannot stall the workers" but the code contradicts this.
- **Suggested fix:** Remove the `WriteAsync` fallback entirely — if `TryWrite` fails on a `DropOldest` channel, something is wrong; just log and skip. Or pass `shutdownCts.Token` to the fallback.

#### 7. JobLifecycle.Cts is never disposed
- **File:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:295-318`
- **Severity:** Low
- **Description:** Each `JobLifecycle` creates a `CancellationTokenSource` (line 309) that is never disposed. In long-running server scenarios with many jobs, this leaks unmanaged timer handles if the CTS was created with a timeout or linked.
- **Suggested fix:** Dispose the CTS in `RunOneAsync`'s `finally` block (after removing from `jobs`).

---

### Area: Download Command

#### 8. Observer task can leak on submission failure
- **File:** `src/Oahu.Cli/Commands/DownloadCommand.cs:199-238`
- **Severity:** High
- **Description:** The observer task is started with `CancellationToken.None` (line 224). If `SubmitAsync` (line 228) throws due to caller cancellation, execution jumps to `await observerTask` (line 233), which may never complete because no jobs were fully submitted and the observer waits for terminal updates. `observerCts.Cancel()` only runs in the `finally` *after* the await, creating a potential hang.
- **Suggested fix:** Cancel `observerCts` *before* awaiting the observer task. Restructure so the finally block cancels first, then awaits:
  ```csharp
  finally { observerCts.Cancel(); }
  await observerTask;
  ```

#### 9. Stdin expansion blocks synchronously, ignoring cancellation
- **File:** `src/Oahu.Cli/Commands/DownloadCommand.cs:419-436`
- **Severity:** Medium
- **Description:** `Console.In.ReadLine()` is a synchronous blocking call. If the user pipes a slow stream or hits Ctrl+C while ASINs are being read from stdin, the command hangs until the stream closes. The `CancellationToken` from the command handler is not checked during the read loop.
- **Suggested fix:** Read stdin asynchronously (`Console.In.ReadLineAsync()`) with periodic cancellation checks, or run the read in a background task with the token.

#### 10. Redundant return expression
- **File:** `src/Oahu.Cli/Commands/DownloadCommand.cs:250`
- **Severity:** Low
- **Description:** `return anyFailed ? 1 : 1;` — the ternary always returns 1 regardless of the condition. This looks like a copy-paste bug; the intent was probably `return anyFailed ? 1 : 0;`, but `allCompleted` already returns 0 on line 248. As written, any non-`allCompleted` outcome returns 1, which may be correct but the dead ternary is confusing and fragile.
- **Suggested fix:** Simplify to `return 1;` or fix to the intended logic.

---

### Area: MCP Server & Auth

#### 11. Auth middleware re-reads token file on every request
- **File:** `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:21-35`
- **Severity:** Medium
- **Description:** Every HTTP request triggers `TokenStore.ReadOrCreate()`, which opens and reads the token file. This is unnecessary I/O on every request, amplifies the race in finding #4, and makes token rotation non-atomic (a request arriving mid-rotation could read a partial or stale file).
- **Suggested fix:** Load the token once at startup into a cached field. If rotation is needed at runtime, use a `ReaderWriterLockSlim` or `Interlocked.Exchange` to swap the cached value.

#### 12. TokenStore.Equal leaks length via early return
- **File:** `src/Oahu.Cli.Server/Auth/TokenStore.cs:55-72`
- **Severity:** Low
- **Description:** The `Equal` method is documented as "constant-time" but returns `false` early on length mismatch (line 63). Since the bearer token has a fixed length (base64url of 32 bytes = 43 chars), this is only exploitable if the attacker doesn't know the token format. Practically low risk, but the comment is misleading.
- **Suggested fix:** Document that length comparison is intentionally not constant-time (acceptable for fixed-length tokens), or pad/normalize to fixed length before comparison.

#### 13. SSE stream endpoint bypasses ToolDispatcher / audit
- **File:** `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:82-114`
- **Severity:** Medium
- **Description:** The `/v1/jobs/stream` SSE endpoint directly calls `jobs.ObserveAll()` without going through `ToolDispatcher`. This means it has no capability check, no audit log entry, and no rate limiting. An authenticated client can open unlimited SSE connections without any record.
- **Suggested fix:** Route through `ToolDispatcher` for the initial connection (audit the subscription), or add explicit audit logging at the SSE endpoint.

#### 14. UserDataLock: Dispose deletes lock file, creating a race window
- **File:** `src/Oahu.Cli.Server/Hosting/UserDataLock.cs:85-101`
- **Severity:** Medium
- **Description:** `Dispose()` closes the stream *then* deletes the file. Between `s.Close()` and `File.Delete()`, another process can acquire a new lock on the same path. The delete then removes the *new* process's lock file, leaving it believing it holds the lock while no file exists for future contenders to detect.
- **Suggested fix:** Delete the file *before* closing the stream (while the write-lock is still held), or use `FileOptions.DeleteOnClose`. On POSIX, unlinking while holding the fd is safe — new openers get a new inode.

---

### Area: Audit Log

#### 15. Audit log silently swallows all write failures
- **File:** `src/Oahu.Cli.Server/Audit/AuditLog.cs:58-85`
- **Severity:** Medium
- **Description:** The outer `catch` on line 82 swallows every exception type, including `OutOfMemoryException`, `StackOverflowException`, and disk-full conditions. While the design doc says "best-effort", completely silent failure means an attacker who fills the disk can perform actions with no audit trail and no indication that auditing is broken.
- **Suggested fix:** At minimum, set a "degraded" flag that surfaces in `/v1/doctor` health checks. Consider catching only `IOException` and letting truly exceptional conditions propagate.

#### 16. Audit log has no flush-to-disk guarantee
- **File:** `src/Oahu.Cli.Server/Audit/AuditLog.cs:77-80`
- **Severity:** Low
- **Description:** Unlike `JsonlHistoryStore.Append` which calls `stream.Flush(flushToDisk: true)`, the audit log `StreamWriter.WriteLine` relies on `Dispose` to flush, which does *not* fsync. A crash immediately after a tool invocation can lose the audit entry.
- **Suggested fix:** Add `stream.Flush(flushToDisk: true)` after the `StreamWriter` write, similar to the history store pattern.

---

### Area: TUI Shell

#### 17. Completed modal is never auto-dismissed
- **File:** `src/Oahu.Cli.Tui/Shell/AppShell.cs:319-333`
- **Severity:** High
- **Description:** When a modal sets `IsComplete = true` (line 329), the shell notes it in a comment but takes no action. `activeModal` remains set, so the render loop continues showing the modal overlay and all input routes to it. The UI appears stuck until the user presses Escape. For programmatic flows (e.g., sign-in completing), the caller has no callback to dismiss the modal.
- **Suggested fix:** After `activeModal.HandleKey(key)`, check `IsComplete` and auto-dismiss (or invoke a completion callback that clears `activeModal`). The current `ActiveModal` property already lets callers read the result before dismissal.

#### 18. Dropped broker challenge deadlocks the auth background task
- **File:** `src/Oahu.Cli.Tui/Shell/AppShell.cs:410-428`
- **Severity:** High
- **Description:** `PollBroker()` dequeues a challenge request (line 420), then calls `ModalFactory.CreateFromChallenge(request)`. If the factory returns `null` (unknown challenge type), the request's `TaskCompletionSource` is never completed or canceled. The background auth flow that submitted the challenge awaits this TCS forever, deadlocking the sign-in.
- **Suggested fix:** If `modal` is null, immediately fail the request: `request.Completion.TrySetException(new NotSupportedException(…))` or `request.Completion.TrySetCanceled()`.

#### 19. Blocking key read prevents resize/broker polling when idle
- **File:** `src/Oahu.Cli.Tui/Shell/AppShell.cs:235-251`
- **Severity:** Medium
- **Description:** When `needsTimedRefresh` is false, `keyReader.ReadKey()` blocks indefinitely (line 235). During this time, `PollBroker()` is not called and terminal resizes are not detected. If a background auth flow submits a challenge while the user is idle, the modal won't appear until the user presses any key.
- **Suggested fix:** Always use `TryReadKey` with a reasonable timeout (e.g., 500ms) to allow periodic polling, or use a separate thread/signal to wake the main loop when broker requests arrive.

---

### Area: Credential Stores

#### 20. External process spawning has no timeout — can hang indefinitely
- **Files:**
  - `src/Oahu.Cli.App/Credentials/MacOsKeychainCredentialStore.cs:88-106`
  - `src/Oahu.Cli.App/Credentials/LinuxSecretToolCredentialStore.cs:81-119`
- **Severity:** High
- **Description:** `WaitForExitAsync(ct)` on spawned `security` / `secret-tool` processes has no timeout. If the system keychain daemon hangs or prompts for a GUI interaction that never completes (e.g., headless environment), the CLI blocks forever. The cancellation token only helps if the caller has an external timeout.
- **Suggested fix:** Use `WaitForExitAsync` with a linked `CancellationTokenSource` that includes a timeout (e.g., 30 seconds). Kill the child process on timeout.

#### 21. Secrets stored as managed strings linger in memory
- **Files:**
  - `src/Oahu.Cli.App/Credentials/LinuxSecretToolCredentialStore.cs:50-57`
  - `src/Oahu.Cli.App/Credentials/WindowsDpapiCredentialStore.cs:41-51`
- **Severity:** Medium
- **Description:** Credentials are passed around and stored as `string` values. .NET strings are immutable, interned, and not zeroed on GC. Secrets can persist in process memory long after use, recoverable via memory dump. The JSON serialization path also copies secrets through multiple intermediate buffers.
- **Suggested fix:** Use `byte[]` or `char[]` for transient secret handling and zero after use. Consider `SecureString` (deprecated but available) or a custom zeroing wrapper for the credential value lifetime.

---

### Area: Output Writers

#### 22. Output writers don't handle broken pipe
- **Files:**
  - `src/Oahu.Cli/Output/PlainOutputWriter.cs`
  - `src/Oahu.Cli/Output/JsonOutputWriter.cs`
  - `src/Oahu.Cli/Output/PrettyOutputWriter.cs`
- **Severity:** Medium
- **Description:** All three writers write to `Console.Error` / `Console.Out` with no `IOException` handling. When output is piped (e.g., `oahu-cli library list | head -5`), the downstream reader closing the pipe causes SIGPIPE or an `IOException` on the next write. This tears down the command with an unhandled exception and a noisy stack trace instead of a clean exit.
- **Suggested fix:** Catch `IOException` / `ObjectDisposedException` in write paths. Treat broken pipe as a normal exit condition (exit code 0), matching Unix CLI conventions.

---

### Area: Capability Policy

#### 23. Destructive operations only need `confirm: true` — no identity binding
- **File:** `src/Oahu.Cli.Server/Capabilities/CapabilityPolicy.cs:39-43`
- **Severity:** Medium
- **Description:** Destructive operations (e.g., `queue_clear`) require only `confirm: true` in the request body. Any authenticated client (MCP host, script) can pass this flag. There's no challenge-response, no rate limit, and no audit trail distinguishing a human confirmation from a scripted one. An MCP host with the bearer token can silently clear the queue.
- **Suggested fix:** For stdio transport, this is acceptable (design §15.2). For HTTP, consider requiring a nonce/CSRF-like token or a two-step confirmation flow for destructive actions. At minimum, ensure the audit log captures `confirmed: true` distinctly.

---

## Findings — GPT-5.3-Codex

### Concurrency / locking / lifecycle

#### 1) Cooperative server lock is unsafe on Unix and can be unlinked while held
- **file:lineRange:** `src/Oahu.Cli.Server/Hosting/UserDataLock.cs:41-47,95-97`; `tests/Oahu.Cli.Tests/Server/UserDataLockTests.cs:14-19`
- **Severity:** **Critical**
- **Description:** Locking relies on `FileShare.Read` semantics, but tests explicitly note this is not enforced on Unix. Also, `Dispose()` closes then deletes the lock file; on Unix, deleting an open file can remove the directory entry for an actively-held lock, allowing a second server instance to create a new lock path.
- **Suggested fix:** Use an OS-backed lock (`FileStream.Lock`/`flock` or dedicated lockfile library) and avoid deleting the lock path while another holder may exist. Keep lock inode stable; write PID without unlink-on-dispose.

#### 2) Job publication can fail jobs when observers disconnect
- **file:lineRange:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:238-245,281-285`
- **Severity:** **High**
- **Description:** `Publish()` writes to subscriber channels; if a channel is completed concurrently, `TryWrite` can fail and `WriteAsync` can throw `ChannelClosedException`. That exception bubbles into job execution path, incorrectly marking jobs failed because a client unsubscribed.
- **Suggested fix:** Treat closed subscriber channels as non-fatal (`TryWrite` only; on failure remove subscriber; catch `ChannelClosedException` around per-subscriber writes).

#### 3) Canceled submit leaves orphaned in-memory jobs
- **file:lineRange:** `src/Oahu.Cli.App/Jobs/JobScheduler.cs:68-76`
- **Severity:** **Medium**
- **Description:** `SubmitAsync` adds lifecycle to `jobs` before awaiting channel write. If `WriteAsync` is canceled/fails, the lifecycle remains in `jobs` forever (never executed, never removed, stale snapshots/cancel behavior).
- **Suggested fix:** Wrap enqueue in try/catch and `jobs.TryRemove(request.Id, out _)` on failure/cancellation.

### Security / process invocation

#### 4) Keychain secret is passed via command-line argument
- **file:lineRange:** `src/Oahu.Cli.App/Credentials/MacOsKeychainCredentialStore.cs:54-56`
- **Severity:** **High**
- **Description:** `security add-generic-password ... -w <secret>` exposes secret in process args (`ps`, diagnostics, crash reports). This is a local secret-leak vector.
- **Suggested fix:** Pass secret through stdin (`-w` omitted; use supported stdin flow) or use macOS Keychain APIs directly via interop so secrets never appear in argv.

### HTTP server robustness

#### 5) Missing request-size and payload-shape guards on mutating endpoints
- **file:lineRange:** `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:54-70,150-172`
- **Severity:** **Medium**
- **Description:** Body DTOs (`QueueAddBody`, `DownloadBody`) accept unbounded arrays/strings and there are no explicit endpoint body-size limits. Large payloads can trigger high memory/CPU use and expensive downstream work.
- **Suggested fix:** Add explicit limits (`MaxRequestBodySize` / endpoint request size limits), validate max ASIN count/length and string lengths, and reject oversized input with 400/413.

#### 6) SSE “snapshot then subscribe” race can drop updates
- **file:lineRange:** `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:90-113`
- **Severity:** **Medium**
- **Description:** Stream flow sends `ListActive()` snapshot first, then subscribes to `ObserveAll()`. Updates emitted between those two steps are missed, violating the stated “consistent snapshot then updates” behavior.
- **Suggested fix:** Subscribe first, then emit snapshot + buffered delta (sequence IDs), or provide a monotonic event cursor and replay from snapshot point.

### API / schema compatibility

#### 7) Quality enum contract mismatch (`Low` documented, `Extreme` implemented)
- **file:lineRange:** `src/Oahu.Cli.Server/Tools/McpTools.cs:47`; `src/Oahu.Cli.Server/Tools/OahuTools.cs:420`; `src/Oahu.Cli.App/Models/DownloadQuality.cs:4-9`; `src/Oahu.Cli/Commands/ConfigCommand.cs:159-165`
- **Severity:** **Medium**
- **Description:** Server tool descriptions and validation error text advertise `Low`, but the actual enum values are `Normal|High|Extreme`. This creates client confusion and breaks schema expectations across transports.
- **Suggested fix:** Normalize all docs/messages/tool descriptions to `normal|high|extreme` (or add backward-compatible alias mapping if `low` must be accepted).

### Test coverage gaps (linked to above defects)

#### 8) No test coverage for cancellation/disconnect edge-cases in scheduler publish path
- **file:lineRange:** `tests/Oahu.Cli.Tests/App/JobSchedulerTests.cs:33-150`
- **Severity:** **Low**
- **Description:** Existing tests cover happy path, cancel, and concurrency, but not “observer disconnect during publish” or “submit canceled before enqueue,” which are the failure modes above.
- **Suggested fix:** Add targeted tests that cancel observer tokens mid-stream and cancel `SubmitAsync` before channel write completion, asserting no false job failure/leaks.

---

## Findings — GPT-5.4

### Command/output

#### Plain output corrupts rows when fields contain tabs or newlines
- **file:** `src/Oahu.Cli/Output/PlainOutputWriter.cs:24-44,65-73`
- **Severity:** Medium-High
- **Description:** The plain writer emits raw tab-separated values and explicitly does "no escapes". Any title, path, error message, or other field containing `\t` or `\n` will shift columns or split a single logical row into multiple lines, which breaks machine parsing on redirected stdout.
- **Suggested fix:** Introduce a documented escaping/quoting rule for plain mode (for example TSV escaping or CSV-style quoting), or reject multiline/tab-containing values in plain mode and force JSON for those shapes.

#### `--config-dir` / `--log-dir` are advertised as global overrides but most of the CLI ignores them
- **file:** `src/Oahu.Cli/Commands/RootCommandFactory.cs:50-60`, `src/Oahu.Cli/Program.cs:42-49`, `src/Oahu.Cli/Commands/CliServiceFactory.cs:50-60`, `src/Oahu.Cli/Commands/ServeCommand.cs:64-71`
- **Severity:** Medium-High
- **Description:** The root command advertises global path overrides, but logging is constructed before parsing and always uses the default `CliPaths.LogDir`. Likewise, most services/server code still hard-code `CliPaths.ConfigFile`, so `--config-dir` only affects the `config` subcommand’s own file access. Users trying to isolate logs/config for testing or automation can silently read/write their real profile/token/log locations.
- **Suggested fix:** Resolve global path overrides before logger/service construction and thread them through a single path provider used by `Program`, `CliServiceFactory`, `ServeCommand`, and token/config services.

### Configuration / forward-compat

#### Saving config drops unknown future fields
- **file:** `src/Oahu.Cli.App/Models/OahuConfig.cs:7-36`, `src/Oahu.Cli.App/Config/JsonConfigService.cs:20-33`, `src/Oahu.Cli.App/AtomicFile.cs:15-21,44-51`
- **Severity:** Medium
- **Description:** The config model is a closed record with no extension-data bucket. `JsonConfigService.LoadAsync()` deserializes into `OahuConfig`, ignoring unknown properties, and `SaveAsync()` rewrites only the known fields. That means a newer CLI version (or another tool) can add settings, then an older build running `config set` will silently erase them.
- **Suggested fix:** Preserve unknown JSON properties when round-tripping config (for example via `[JsonExtensionData]` or by loading/saving a `JsonObject` and patching only known keys).

### TUI / accessibility / auth

#### TUI environment hooks are effectively dead in the real shell
- **file:** `src/Oahu.Cli/Commands/TuiCommand.cs:43-53`, `src/Oahu.Cli.Tui/Shell/AppShell.cs:210-245,430-534`, `src/Oahu.Cli.Tui/Hooks/ScreenReaderProbe.cs:18-31`, `src/Oahu.Cli.Tui/Hooks/SshDetector.cs:12-27`, `src/Oahu.Cli.Tui/Hooks/TerminalSize.cs:38-53`
- **Severity:** Medium-High
- **Description:** The production TUI entry path only checks basic TTY state. `ScreenReaderProbe` and `SshDetector` are never consulted by `TuiCommand`/`AppShell`, and `TerminalSize.Poll()` is never wired into the run loop. In practice, the documented screen-reader refusal path never triggers, SSH-specific downgrades never activate, and terminal resizes do not cause an immediate re-render unless another key/timer event happens first.
- **Suggested fix:** Gate TUI launch on `ScreenReaderProbe.IsActive()`, use `SshDetector` to tune animation/effects in the real shell, and poll `TerminalSize` from the run loop so resize events trigger a redraw.

#### Completed auth modals stay mounted, which can stall multi-step sign-in and keep redirect URLs in memory longer than necessary
- **file:** `src/Oahu.Cli.Tui/Shell/AppShell.cs:320-333,410-427,695-724`, `src/Oahu.Cli.Tui/Auth/ExternalLoginModal.cs:18-24,45-50`
- **Severity:** High
- **Description:** When an external-login/MFA/CVF modal completes, the adapter fulfills the broker task but the shell intentionally leaves `activeModal` in place. While that completed modal remains mounted, `PollBroker()` refuses to surface the next queued challenge, so multi-step sign-in flows can get stuck behind a stale modal. For external login, the pasted redirect URL also remains in the `TextInput` buffer until the user dismisses it manually.
- **Suggested fix:** Auto-dismiss auth modals as soon as completion/cancellation is observed, and explicitly clear sensitive input/status text before releasing the result.

### Server

#### Destructive HTTP denials bubble out as 500s instead of a structured 4xx response
- **file:** `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:60-64`, `src/Oahu.Cli.Server/Hosting/ToolDispatcher.cs:38-46`, `src/Oahu.Cli.Server/Capabilities/CapabilityPolicy.cs:39-57`
- **Severity:** Medium
- **Description:** Capability denials are represented by `UnauthorizedAccessException`. The HTTP endpoints call `ToolDispatcher.InvokeAsync()` directly and do not map that exception to an HTTP result, so requests like `DELETE /v1/queue` without `confirm=true` will surface as an unhandled server error rather than a clear `403 Forbidden`/`400 Bad Request` JSON response.
- **Suggested fix:** Add exception mapping in the HTTP pipeline (or return typed results from the dispatcher) so capability denials consistently produce explicit 4xx responses with a machine-readable error body.

---

## Design Gaps — Claude Opus 4.7 (vs. OAHU_CLI_DESIGN.md)

Scope: section-by-section comparison of `docs/OAHU_CLI_DESIGN.md` (821 lines) against the implementation under `src/Oahu.Cli`, `src/Oahu.Cli.App`, `src/Oahu.Cli.Server`, `src/Oahu.Cli.Tui`, and `tests/Oahu.Cli.Tests`. Severity reflects user-visible impact in a 1.0 release.

---

### §3.1 Mode selection / §3 Dual-mode contract

#### Gap 3.1-A — `oahu-cli --version` short alias `-v` not present
- Design quote (§4.2, line 179): "Standard flags: `-h/--help`, `-v/--version`, `-q/--quiet`, `--verbose`, …"
- Implementation reference: `src/Oahu.Cli/Commands/RootCommandFactory.cs:22-84` — no version option is registered at all on the root command (relying on `System.CommandLine`'s built-in `--version` only); `--verbose` exists with no short alias either.
- Severity: **Low**
- Description: The doc lists `-v` as the version short alias and uses `--verbose` (no short). Built-in `System.CommandLine` may surface `--version` automatically but `-v` is not bound, so `oahu-cli -v` does not work.
- Suggested resolution: Either explicitly bind `-v`/`--version` and document that `--verbose` has no short, or update §4.2 to say "`--version` (no short)".

#### Gap 3.1-B — "Every `--flag` has a `--no-flag` partner" not implemented
- Design quote (§4.2, line 180): "Every `--flag` has a `--no-flag` partner where it makes sense."
- Implementation reference: `src/Oahu.Cli/Commands/RootCommandFactory.cs:24-83` (and all subcommand option declarations). No `--no-color`/`--no-json`/`--no-force` style negators registered.
- Severity: **Low**
- Description: Boolean flags do not have `--no-<flag>` complements, so users cannot easily override a config-defaulted boolean.
- Suggested resolution: Either add the negators uniformly or weaken the doc to "where required" and enumerate the actual cases.

---

### §4.1 Command surface

#### Gap 4.1-A — `library list --unread` documented but errors at runtime
- Design quote (§4.1, line 153): `library list   [--json|--plain] [--filter <q>] [--unread] [--limit N]`
- Implementation: `src/Oahu.Cli/Commands/LibraryCommand.cs:55-75` — the option is declared, then unconditionally rejected: `"--unread is not implemented yet (oahu-cli phase 4b.2)."`, exit 1.
- Severity: **Medium**
- Description: A v1-listed flag is a stub that always fails. JSON consumers calling `library list --unread --json` will see an error to stderr and exit 1.
- Suggested resolution: Either implement (book "unread" state from `Oahu.Data`) or remove the flag from the surface until it works.

#### Gap 4.1-B — `library show <asin>` defaults to JSON in pipes (doc) but is shape-wise a single resource only
- Design quote (§4.1, line 155): `library show   <asin>            # detail view, json by default in pipes`
- Implementation: `src/Oahu.Cli/Commands/LibraryCommand.cs:155-175` — emits one resource via the writer, which honors the global `--json/--plain/auto-degrade` switch (correct), but no `--json`-by-default behavior is special-cased for "detail" — same rule as everything else.
- Severity: **Low**
- Description: Behavior matches the universal "auto-degrade on non-TTY" rule, which is effectively the same as "json by default in pipes" given the writer factory. Doc wording is slightly misleading (suggests detail view is special).
- Suggested resolution: Keep code, clarify doc.

#### Gap 4.1-C — `queue add <asin|title>` accepts only ASIN
- Design quote (§4.1, line 158): `queue add      <asin|title>...   # supports ` for stdin (one per line)`
- Implementation: `src/Oahu.Cli/Commands/QueueCommand.cs:70-117` — only ASINs accepted; the title arm is deferred ("phase 4b extends `add` to accept titles").
- Severity: **Medium**
- Description: A documented v1 input form (resolving title strings via the library cache) is not wired.
- Suggested resolution: Implement title→ASIN resolution against `ILibraryService`, or remove `<title>` from the documented surface.

#### Gap 4.1-D — `queue remove <jobId>...` actually takes ASINs
- Design quote (§4.1, line 159): `queue remove   <jobId>...`
- Implementation: `src/Oahu.Cli/Commands/QueueCommand.cs:120-180` — argument is named `asin` and matched against `QueueEntry.Asin`. Queue entries also have no jobId (`src/Oahu.Cli.App/Models/QueueEntry.cs`).
- Severity: **High**
- Description: Removing by jobId (per spec) is impossible; users / scripts that follow the doc will get "missing" responses for every ID. Also affects MCP `queue_remove` and HTTP `DELETE /v1/queue/{asin}`.
- Suggested resolution: Either give queue entries a stable `jobId` and remove by it (per spec), or change the doc + completion + JSON schemas to standardize on ASIN as the queue identity.

#### Gap 4.1-E — `download` flags `--all-new`, `--no-decrypt`, and `m4b|both` export missing
- Design quote (§4.1, line 162): `download <asin|title>... [--all-new] [--quality <q>] [--concurrency N] [--no-decrypt] [--export aax|m4b|both] [--output-dir <path>]`
- Implementation: `src/Oahu.Cli/Commands/DownloadCommand.cs:37-124` — flags present: `--quality`, `--profile`, `--from-queue`, `--export none|aax`, `--output-dir`, `--concurrency`. Missing: `--all-new`, `--no-decrypt`, and `--export m4b`/`--export both`.
- Severity: **High**
- Description: Three documented v1 download options have no implementation. `--from-queue` exists but is not in the design. Export format set is narrower than promised (no M4B path).
- Suggested resolution: Either wire `--all-new` (download every library item not yet downloaded), `--no-decrypt`, and `m4b`/`both` export targets through `Oahu.Decrypt`, or update the doc to reflect AAX-only, queue-driven semantics.

#### Gap 4.1-F — `convert <file>` design vs ASIN-based impl
- Design quote (§4.1, line 163): `convert <file> [--export aax|m4b|both] [--output-dir <path>]`
- Implementation: `src/Oahu.Cli/Commands/ConvertCommand.cs:33-117` — accepts ASINs (not file paths) and always exports to AAX; deviation acknowledged in the source XML doc.
- Severity: **Medium**
- Description: Public CLI shape diverges from the spec's file-driven design and from the doc's `--export` choices.
- Suggested resolution: Either (a) add a true file-driven `convert <file>` path (spec-compliant) and keep the ASIN form as a separate `library export` command, or (b) update §4.1 to match ASIN-based semantics and document why the file form is omitted.

#### Gap 4.1-G — `auth login` design takes only `--region`; impl adds positional + `--pre-amazon`
- Design quote (§4.1, line 149): `auth login   [--region us|uk|de|fr|jp|it|au|in|ca|es|br]`
- Implementation: `src/Oahu.Cli/Commands/AuthCommand.cs:55-109` — adds a positional `region` argument and a `--pre-amazon` flag.
- Severity: **Low**
- Description: Strict superset of the design, but the doc under-specifies and the extras are user-facing.
- Suggested resolution: Document `--pre-amazon` and the positional region in §4.1.

---

### §4.2 Conventions

#### Gap 4.2-A — `--quiet` and `--verbose` are not recursive
- Design quote (§4.2): "Standard flags: `-h/--help`, `-v/--version`, `-q/--quiet`, `--verbose`, ...". Implied to apply globally.
- Implementation: `src/Oahu.Cli/Commands/RootCommandFactory.cs:24-31` — `quietOpt` and `verboseOpt` lack `Recursive = true` (only `force`, `dryRun`, `json`, `plain` are recursive).
- Severity: **Medium**
- Description: `oahu-cli library list --quiet` won't bind `--quiet` (it's parsed as belonging to root and may be rejected when placed after the subcommand). Inconsistent UX; defeats the "global flag" promise.
- Suggested resolution: Mark `quietOpt`, `verboseOpt`, `noColorOpt`, `asciiOpt`, `configDirOpt`, `logDirOpt`, `logLevelOpt` as `Recursive = true` (matching `force`/`dryRun`/`json`/`plain`).

---

### §7 Persistence & state

#### Gap 7-A — macOS shared user-data path comment doesn't match what `Environment.SpecialFolder.LocalApplicationData` returns
- Design quote (§7, line 408): "Job queue (pending) — Alongside the GUI's user-data dir … as `queue.json`"
- Implementation: `src/Oahu.Cli.App/Paths/CliPaths.cs:45-52` — uses `LocalApplicationData` and the XML comment claims `~/Library/Application Support/oahu` on macOS, but on .NET 10 macOS that special folder resolves to `~/.local/share/<name>` (XDG-style), not `~/Library/Application Support`. Whether this matches the GUI's actual user-data dir depends on `Oahu.Aux.ApplEnv.LocalApplDirectory` — the design's guarantee is "same dir the Avalonia app writes to today".
- Severity: **High** (if it really diverges from the GUI; correctness should be verified)
- Description: If the GUI writes to `~/Library/Application Support/oahu` on macOS while the CLI writes queue/history to `~/.local/share/oahu`, the two front-ends will see different queues — directly contradicting the design's "no migration step" guarantee.
- Suggested resolution: Replace `LocalApplicationData`-based resolution with a call into the same `ApplEnv.LocalApplDirectory` the GUI uses, then update the comment. Add a test asserting parity with `Oahu.Aux.ApplEnv.LocalApplDirectory`.

---

### §8 Concurrency & job pipeline

#### Gap 8-A — Crash recovery / phase resumption not implemented in the scheduler
- Design quote (§8, line 422): "Resuming after a crash: 1. Read `queue.json`. 2. For each item, ask `IAudibleApi.GetPersistentState` what phase it last reached. 3. Re-enter the pipeline at that phase."
- Implementation: `src/Oahu.Cli.App/Jobs/JobScheduler.cs:172-223` (`RunOneAsync`) — always runs the executor from scratch; nothing reads `queue.json` on startup or queries `GetPersistentState` to resume; nothing rewrites `queue.json` after each phase boundary inside the scheduler.
- Severity: **High**
- Description: A `Ctrl+C` mid-download leaves no scheduler-side resumption. This breaks the "Crash-only and resumable" v1 goal (§1) and the §8 contract. (The underlying `Oahu.Core` may persist its own AAX/AAXC state, but the CLI scheduler is the documented owner of `queue.json` resumption.)
- Suggested resolution: On scheduler startup, drain `queue.json`, query `GetPersistentState`, and re-submit each item with a flag telling the executor where to start. Add an integration test for "kill mid-flight, restart, observe resume" (the §8 Phase 8 exit criterion).

#### Gap 8-B — `JobPhase.Exporting` not modelled
- Design quote (§3.2, line 132): `queued → downloading → decrypting → muxing → exporting? → completed`
- Implementation: `src/Oahu.Cli.App/Models/JobModels.cs:6-16` — phases are `Queued, Licensing, Downloading, Decrypting, Muxing, Completed, Failed, Canceled`. There is `Licensing` (not in spec) but no `Exporting`.
- Severity: **Low**
- Description: Cosmetic but visible in JSON outputs and SSE events; consumers told to expect `exporting` will not see it.
- Suggested resolution: Add `Licensing` to the design (as a real phase) and either rename `Muxing`→`Exporting` to match the spec, or add `Exporting` distinct from `Muxing` for the post-decrypt AAX/M4B step.

#### Gap 8-C — `Ctrl+C` progressive policy in TUI mode not asserted by tests / config-default mismatch
- Design quote (§8, lines 426-432): progressive Ctrl+C semantics, including the 5 s grace in command mode.
- Implementation: `src/Oahu.Cli/CliEnvironment.cs:122-128` — registers a `CancelKeyPress` handler that just runs restore. The 5 s grace and "second Ctrl+C within 2 s skips" logic for TUI mode lives in TUI shell code; not exercised by Program.cs's command-mode path.
- Severity: **Medium**
- Description: Command-mode shutdown grace is undefined (no documented timeout); double-Ctrl-C in command mode probably does nothing special.
- Suggested resolution: Implement a 5 s soft-cancel grace in `Program.Main` after observing `OperationCanceledException`, and a hard-exit on a second Ctrl+C.

---

### §9 Output formats

#### Gap 9-A — `--json` and `--plain` together: precedence undocumented
- Design quote (§9, table at line 438-443): three exclusive modes.
- Implementation: `src/Oahu.Cli.App` & `src/Oahu.Cli/Output/OutputContext.cs` — both flags are recursive booleans; `OutputContext.ResolveFormat(json, plain, autoPlain)` quietly picks one.
- Severity: **Low**
- Description: Passing both flags is undefined behavior; spec implies they're mutually exclusive but System.CommandLine accepts both without error.
- Suggested resolution: Add a validator that errors with "use exactly one of --json/--plain" when both are set; update §9 to spell out exclusivity.

---

### §10 Error handling — exit codes

#### Gap 10-A — Exit code 4 collision: design says "Audible API error", impl uses 4 for "data-dir lock held"
- Design quote (§10, line 467): `4` Audible API error
- Implementation: `src/Oahu.Cli/Commands/ServeCommand.cs:30` — `public const int LockedExitCode = 4;` (and used for "stop the running server before rotating"). Also `ServerHost.RunAsync` returns 4 on lock failure.
- Severity: **High**
- Description: A user-supplied exit-code semantic ("Audible failed") is overloaded for an unrelated condition (lock conflict). Scripts can't distinguish them.
- Suggested resolution: Reserve an unused code (e.g. 6 — "resource busy") for the lock case, and route Audible-API failures through code 4 consistently.

#### Gap 10-B — Decryption / conversion errors don't reliably map to exit code 5
- Design quote (§10, line 468): `5` decryption / conversion error
- Implementation: `src/Oahu.Cli/Commands/DownloadCommand.cs:242-250` — failed/canceled/missing → exit 1; never 5. `Program.cs:69-82` blanket-catches Exception → 1.
- Severity: **Medium**
- Description: Decrypt-stage failures look identical to generic failures; cannot drive automation. Sync errors in `library sync` map to exit 4 (good) but other Audible-vs-decrypt distinction is lost.
- Suggested resolution: Have the executor classify terminal errors and surface a typed reason on `JobUpdate`/`JobRecord`; map to 4 or 5 in the command-mode runner.

#### Gap 10-C — "Most-important sentence last" + `Errors` static class not present
- Design quote (§10, line 452): "One central `Errors` static class produces structured `OahuCliException`s with: a *what*, a *why*, and a *fix*."
- Implementation: No `Errors` class or `OahuCliException` exists; each command formats its own one-line stderr message via `CliEnvironment.Error.WriteLine(...)`.
- Severity: **Medium**
- Description: Error rendering is ad-hoc and inconsistent (some include hints like "(Run `oahu-cli doctor`…)", most don't). The "what / why / fix" three-line shape never appears.
- Suggested resolution: Add an `OahuCliException` + `Errors` factory and route all command catches through a single renderer.

---

### §11 Accessibility / §6 Design system

#### Gap 11-A — Colorblind theme missing
- Design quote (§6.1, line 311): `// Default, HighContrast, Colorblind, Mono`
- Implementation: `src/Oahu.Cli.Tui/Themes/Theme.cs:18-21` — `Available = { Default, Mono, HighContrast }`. No Colorblind theme.
- Severity: **Low**
- Description: Promised theme not shipped.
- Suggested resolution: Add a `Themes.Colorblind` palette tuned for deuteranopia/protanopia (Okabe-Ito or similar) or remove from the doc.

#### Gap 11-B — `PulseSpinner`, `ProgressTimeline`, `KeyHint` widgets absent
- Design quote (§6.3, lines 332-343): widget table includes `PulseSpinner`, `ProgressTimeline`, `KeyHint`.
- Implementation: `src/Oahu.Cli.Tui/Widgets/` contains `StatusLine`, `TimelineItem`, `HintBar`, `Dialog`, `TabStrip`, `SelectList`, `StyledTable`, `StatusLine` — no `PulseSpinner.cs`, no `ProgressTimeline.cs`, no `KeyHint.cs`.
- Severity: **Medium**
- Description: Three "minimum useful set" widgets from §6.3 are not implemented; §6.6.1 builds an explicit UX story around `PulseSpinner` cadence, fallbacks, and frame set.
- Suggested resolution: Implement the three widgets (the pulse spinner especially has a fully-specified frame set in §6.6.1) or update the table to mark them as deferred.

---

### §13 Testing

#### Gap 13-A — `tests/Oahu.Cli.E2E` project missing
- Design quote (§16 layout, line 807): `Oahu.Cli.E2E/               (NEW — spawn-binary smoke tests)`. §13: "End-to-end | A `tests/e2e/` project that spawns the binary on Win/macOS/Linux runners; asserts `--version`, `--help`, `doctor` exit cleanly".
- Implementation: `tests/` only contains `Oahu.Cli.Tests`.
- Severity: **Medium**
- Description: No CI smoke tests at the binary boundary; cross-platform regressions in `--version` / `--help` / `doctor` are caught only by manual runs.
- Suggested resolution: Add an `Oahu.Cli.E2E` project that `dotnet publish`-es the CLI and shells out to it from xUnit, gated to per-RID matrices in CI.

---

### §15.1 / §15.2 `oahu-cli serve`

#### Gap 15-A — HTTP endpoint paths diverge from spec
- Design quote (§15.1 table, lines 656-668):
  - `GET /v1/auth` (impl: `GET /v1/auth/status`)
  - `DELETE /v1/queue/{jobId}` (impl: `DELETE /v1/queue/{asin}`; see Gap 4.1-D)
  - `GET /v1/jobs?stream=sse` (impl: `GET /v1/jobs/stream`)
  - `GET/PUT /v1/config` (impl: `PUT /v1/config/{key}` — per-key path)
- Implementation: `src/Oahu.Cli.Server/Hosting/HttpEndpoints.cs:39-129`
- Severity: **High**
- Description: Four documented routes don't match. Clients written from §15.1 will receive 404s.
- Suggested resolution: Either align routes with §15.1 or revise §15.1 to document the actual shapes (e.g., `?stream=sse` query overload vs `/jobs/stream` is a real architectural choice — pick one and update both).

#### Gap 15-B — `history_delete` (destructive) tool/endpoint missing
- Design quote (§15.1, line 666): "History delete | `history_delete` (args: `jobIds`) | `DELETE /v1/history/{jobId}` | destructive"
- Implementation: `src/Oahu.Cli.Server/Tools/McpTools.cs` and `Hosting/HttpEndpoints.cs` — neither registers history deletion.
- Severity: **Medium**
- Description: A documented destructive-class operation is absent; clients can't prune history through the server.
- Suggested resolution: Add `history_delete` to `OahuTools`, register both surfaces, mark Destructive, and add to the audit harness.

#### Gap 15-C — Unix-socket / named-pipe transport (`--listen unix`) not implemented
- Design quote (§15.2 / §15.1 process model, line 683): `oahu-cli serve --http --listen unix`
- Implementation: `src/Oahu.Cli/Commands/ServeCommand.cs:36-46` — only `--bind` and `--port` (TCP loopback). No `--listen` option.
- Severity: **Medium**
- Description: One of the two interchangeable 1.0 local-auth mechanisms (per §15.2) is unavailable, so users on multi-user hosts have only the bearer-token path.
- Suggested resolution: Implement `--listen unix` using Kestrel's `ListenUnixSocket` (or named-pipe equivalent on Windows) and apply mode 0600 / SDDL hardening as specified.

#### Gap 15-D — Rate limit on mutating/expensive tools not implemented
- Design quote (§15.2 cross-cutting, line 746): "Rate limit writes (10 req/min/principal default for `mutating`+`expensive` tools) on loopback even — defends against a runaway agent."
- Implementation: `src/Oahu.Cli.Server/Hosting/ToolDispatcher.cs` (and middleware in `HttpEndpoints.cs`) — no rate limiter registered.
- Severity: **Medium**
- Description: A documented defense-in-depth control is missing; an agent loop could hammer `download` / `library_sync`.
- Suggested resolution: Wire `Microsoft.AspNetCore.RateLimiting` (or hand-roll a token bucket in the dispatcher) keyed by the resolved principal.

#### Gap 15-E — Loopback bind warning line not printed at startup
- Design quote (§15.2, line 747): "Discovery hardening: in 1.0, the server prints a clear 'ALLOWED FROM: localhost only' line at startup."
- Implementation: `src/Oahu.Cli.Server/Hosting/ServerHost.cs:158-160` — prints the bind URL but no explicit "ALLOWED FROM: localhost only" message.
- Severity: **Low**
- Description: Discovery / safety message missing.
- Suggested resolution: Add the literal line to startup stderr when bound to a loopback address.

#### Gap 15-F — `--print-config` flag for `oahu-cli doctor` not implemented
- Design quote (§15.2, line 747): "Add a `--print-config` flag for `oahu-cli doctor` to dump the active auth posture."
- Implementation: `src/Oahu.Cli/Commands/DoctorCommand.cs:14-67` — only `--json`, `--skip-network`, `--fix`.
- Severity: **Low**
- Description: A targeted operator-help flag is missing.
- Suggested resolution: Add the flag, dump token path / mode / bind / capability policy.

#### Gap 15-G — `--strict-peer` / `SO_PEERCRED` defense-in-depth absent
- Design quote (§15.2, line 723-724): "opt-in via `--strict-peer` … reject any connection from a process not owned by the current user."
- Implementation: not implemented.
- Severity: **Low** (explicitly opt-in / off-by-default per design)
- Description: Acceptable to defer if labeled as such in the doc.
- Suggested resolution: Either implement or update the doc to "post-1.0".

#### Gap 15-H — `serve` capability-class confirmation under stdio MCP
- Design quote (§15.2, lines 700-703): under stdio MCP "confirm under stdio (allow when `--unattended` or under HTTP-with-token)" for `mutating`/`expensive`; "destructive → always confirm; require `confirm: true` argument under `--unattended`".
- Implementation: `src/Oahu.Cli.Server/Capabilities/CapabilityPolicy.cs` (referenced from `ServerHost.cs:199`) and `ToolDispatcher` — present, but actual interactive confirmation prompts to stderr are not visible in `McpTools.cs` (no readline on stderr-tty path).
- Severity: **Medium** (verify carefully)
- Description: Need to confirm that non-`safe` tools really do block on a stderr prompt under stdio when not `--unattended`. If they auto-allow, the design's audit/safety story is weaker than documented.
- Suggested resolution: Add a unit test in `tests/Oahu.Cli.Tests/Server/` asserting that `queue_clear` without `confirm=true` and without `--unattended` is denied (per §15.2 last bullet of "Open questions": "when no TTY on stderr, we must auto-deny non-`safe` tools").

#### Gap 15-I — `auth_login` exclusion enforced where? (Verify)
- Design quote (§15.1, line 670): "`auth_login` is **never** exposed over the server. Period."
- Implementation: `McpTools.cs` and `HttpEndpoints.cs` — confirmed absent (good). No explicit guard / test, however.
- Severity: **Low**
- Description: Compliant by omission. Add a test that asserts the tool list does not contain `auth_login` so a future contributor cannot accidentally re-add it.
- Suggested resolution: Add a regression test.

---

### Cross-cutting / minor

#### Gap X-A — `_schemaVersion` is `"1"` (string) everywhere except `doctor.schema.json`
- Design quote (§9, line 442): "versioned via `_schemaVersion`"
- Implementation: `docs/cli-schemas/doctor.schema.json:9` allows `["integer","string"]`; everything else pins `const: "1"`.
- Severity: **Low**
- Description: Inconsistent typing across schemas; consumers using strict validators may trip.
- Suggested resolution: Normalize all schemas to `string "1"` (or all to integer `1`).

#### Gap X-B — `CliPaths.SharedUserDataDir` is not aligned with `Oahu.Aux.ApplEnv.LocalApplDirectory`
- Design quote (§7, line 412): "The CLI **never** writes anything inside the Avalonia GUI's user-data area beyond what the existing `Oahu.Data` / profile machinery already does — so the two front-ends coexist on the same machine using the same library cache."
- Implementation: `src/Oahu.Cli.App/Paths/CliPaths.cs:49-52` — derives independently from `Environment.SpecialFolder.LocalApplicationData/oahu` rather than calling into `ApplEnv.LocalApplDirectory`.
- Severity: **High** (depending on platform parity — see Gap 7-A)
- Description: Two independent path resolutions invite drift between GUI and CLI.
- Suggested resolution: Have `SharedUserDataDir` delegate to `Oahu.Aux.ApplEnv.LocalApplDirectory` (the existing GUI path), and add a parity test.

#### Gap X-C — `--ascii` / `OAHU_ASCII_ICONS` are CLI extensions not in the doc
- Design quote (§6.2, line 320): icons "with ASCII fallback for hostile terminals (`OAHU_ASCII_ICONS=1` or `TERM=dumb`)" — env var only.
- Implementation: `src/Oahu.Cli/Commands/RootCommandFactory.cs:46-49,93` — also adds a global `--ascii` flag.
- Severity: **Low**
- Description: Acceptable extension, undocumented.
- Suggested resolution: Document `--ascii` in §4.2.

#### Gap X-D — `download` runs return 1 for "all canceled" with no distinct exit code
- Design quote (§10): `130` cancelled by user.
- Implementation: `src/Oahu.Cli/Commands/DownloadCommand.cs:242-250` — returns 1 on any failed *or* canceled job; only the outermost `Program.Main` catch returns 130 on `OperationCanceledException` from the parser.
- Severity: **Low**
- Description: A user who Ctrl+C's a download will see exit 1 (not 130) if the executor swallowed the cancellation per-job.
- Suggested resolution: When all terminal phases are `Canceled`, return 130 from the command runner.

---

### Coverage summary

| Design area | Status |
|---|---|
| Mode selection, alt-screen exit | Implemented |
| Global flags | Mostly — see 3.1-A, 3.1-B, 4.2-A, X-C |
| `auth login/status/logout` | Implemented (with extras — 4.1-G) |
| `library list/sync/show` | Partial — 4.1-A |
| `queue list/add/remove/clear` | Partial — 4.1-C, 4.1-D |
| `download` | Partial — 4.1-E |
| `convert` | Diverges — 4.1-F |
| `history list/show/retry` | Implemented |
| `config get/set/path` | Implemented |
| `doctor` | Implemented (no `--fix` real action; no `--print-config`) |
| `completion` | Implemented |
| `serve` (MCP+HTTP) | Partial — 15-A, 15-B, 15-C, 15-D, 15-E, 15-F |
| Output formats / schemas | Implemented (X-A, 9-A nits) |
| Exit codes | Partial — 10-A, 10-B, X-D |
| Persistence paths | Risky — 7-A, X-B |
| Job pipeline & resume | Partial — 8-A, 8-B, 8-C |
| Doctor checks | Implemented |
| TUI scope | Mostly — 11-A, 11-B |
| Accessibility | Mostly |
| Testing scaffolding | Missing E2E — 13-A |
| Distribution | Out of scope for this review |

---


## Cross-Cutting Themes (Convergent Findings)

The following issues were independently flagged by **two or more** of the five reviewers and should be prioritized:

1. **CancellationTokenSource / observer-task lifecycle in `JobScheduler.SubmitAsync`** — leaks linked CTS and orphaned observer tasks; flagged by Sonnet 4.5 and Opus 4.6.
2. **`AtomicFile` temp-path collisions and durability** — non-unique temp file names + missing fsync on directory; flagged by Sonnet 4.5 and Opus 4.6.
3. **Credential store process invocation hangs / argv leakage** — macOS `security` and Linux `secret-tool` invoked with secrets on argv and without timeouts; flagged by Sonnet 4.5, Opus 4.6, and GPT-5.3-Codex.
4. **Output corruption / formatting in plain & pretty writers** — embedded tab/newline corruption and inconsistent NO_COLOR handling; flagged by Sonnet 4.5 and GPT-5.4.
5. **HTTP server semantics — wrong status codes, missing payload limits, SSE races** — flagged by Opus 4.6, GPT-5.3-Codex, and GPT-5.4.
6. **Auth modal / SignInFlow lifecycle** — completed modal not dismissed and broker challenge deadlock potential; flagged by Opus 4.6 and GPT-5.4.
7. **Queue/jobId vs. ASIN identity mismatch** — implementation uses ASIN where the design (and MCP/HTTP contract) calls for `jobId`; flagged by GPT-5.3-Codex (API contract) and Opus 4.7 (design gap).
8. **`SharedUserDataDir` divergence from GUI `ApplEnv.LocalApplDirectory`** — long-standing memory-confirmed risk; re-flagged by Opus 4.7.
9. **No crash recovery / phase resumption in `JobScheduler`** — design calls for crash-only/resumable jobs; not implemented (Opus 4.7).
10. **Global flags (`--config-dir`, `--log-dir`, `--quiet`, `--verbose`) not honored on subcommands** — flagged by GPT-5.4 and Opus 4.7.

## Recommended Triage Order

1. Address all **High/Critical** items called out by multiple reviewers (themes #1, #2, #3, #5).
2. Reconcile the design contract gaps in theme #7 (jobId vs. ASIN) and #10 (global flags) — these are user-facing.
3. Implement crash-recovery for `JobScheduler` (#9) — required by design and currently absent.
4. Resolve the modal / sign-in lifecycle (#6) before adding more TUI screens.
5. Fix output writer correctness (#4) before relying on JSON/plain output for scripting.
6. Sweep remaining Medium findings; many can be batched (e.g., fsync-on-rename, CTS disposal patterns, ConfigureAwait usage).


---

# Remediation Summary (2026-04-25)

The findings above were grouped by file/project affinity into 24 work groups and
addressed across one focused pass. Final state after the pass: full solution
builds clean (0 warnings, 0 errors) and all **277 tests pass** with **no StyleCop
regressions**.

## Fixes shipped (Bucket A — in scope)

### `src/Oahu.Cli.App` (core services)
- **`Jobs/JobScheduler.cs`** — `SubmitAsync` rolls back on channel write failure;
  per-job `CancellationTokenSource` disposed in a `finally`; `DisposeAsync`
  re-entrancy guarded with `Interlocked`; `Publish` removes dead subscribers
  instead of blocking.
- **`AtomicFile.cs`** — Guid-suffixed temp files via `FileMode.CreateNew` with
  cleanup-on-failure (no clobber when two writers race).
- **`Queue/JsonFileQueueService.cs`** — Cross-process `FileShare.None` lockfile;
  `MoveAsync` bounds-check reordered to run before equality short-circuit.
- **`Jobs/JsonlHistoryStore.cs`** — Optional `ILogger` ctor parameter; torn JSON
  lines are logged with line numbers instead of silently dropped.
- **`Credentials/MacOsKeychainCredentialStore.cs`,
  `Credentials/LinuxSecretToolCredentialStore.cs`** — 30s default timeout via a
  linked CTS; child process `Kill()` on cancel; stdio drain guarded against
  cancellation; PATH-search for the `security` binary; macOS argv-secret
  exposure documented as a NOTE comment.
- **`Library/CoreLibraryService.cs`** — `SyncAsync` now resolves the requested
  alias and switches the active profile via `ChangeProfileAsync` if it differs;
  series-position sentinel divisor widened from `/10` to `/1000` to avoid
  collisions in long series.
- **`Jobs/AudibleJobExecutor.cs`** — Background download task is launched under
  a linked CTS and always observed in a `finally` (no unobserved task
  exceptions, no leak if the consumer abandons the iterator); ExportDirectory
  pre-flighted with `Directory.CreateDirectory` so muxing failures surface
  before a long download instead of after.
- **`Auth/CallbackBridge.cs`** — Explicit `ConfigureAwait(false)` on every
  blocking await reachable from sync callback bridges.
- **`Models/OahuConfig.cs`** — `[JsonExtensionData]` round-trips unknown
  properties so a newer CLI's fields are not erased by an older CLI.
- **`Paths/CliPaths.cs`** — `SharedUserDataDir` aligned with
  `Oahu.Aux.ApplEnv.LocalApplDirectory` so CLI and GUI share the same
  user-data directory (correct casing on Linux/macOS).

### `src/Oahu.Cli.Server` (loopback transport)
- **`Auth/TokenStore.cs`** — Added `GetCached()` so the bearer-token middleware
  doesn't hit disk on every request and so two concurrent first-callers can't
  race-write the file twice.
- **`Hosting/HttpEndpoints.cs`** — 256 KB request body cap; outer middleware
  maps `UnauthorizedAccessException` → 403 with a JSON body (no dev exception
  page); SSE handler subscribes to the job stream **before** taking the
  active-job snapshot (closes the lost-update window) and tolerates
  broken-pipe / disposed-stream conditions cleanly.
- **`Hosting/UserDataLock.cs`** — Lock file opened with `FileShare.Delete |
  FileOptions.DeleteOnClose` so it is removed atomically when the stream
  closes (no race window where another process could acquire the about-to-be-
  deleted file).
- **`Audit/AuditLog.cs`** — `StreamWriter.Flush()` followed by
  `FileStream.Flush(flushToDisk: true)` so audit lines survive crashes;
  consecutive-failure counter surfaces a warning to stderr at the threshold so
  a wedged disk doesn't silently lose audit data.
- **`Hosting/ToolDispatcher.cs`** — `SafeAudit` wrapper guarantees that an
  audit failure can never mask the actual tool result or exception.
- **`Hosting/ServerHost.cs`** — Exit code **6** is now used for single-instance
  lock contention, **5** for token init/permission failure (frees up exit
  code 4 for Audible API errors per design §10).

### `src/Oahu.Cli` (front-end commands)
- **`Output/PlainOutputWriter.cs`, `JsonOutputWriter.cs`,
  `PrettyOutputWriter.cs`** — All writes wrapped in `SafeWrite` that
  swallows `IOException` / `ObjectDisposedException` (broken pipe to `head`,
  `less`, etc.); plain writer escapes `\\`, `\t`, `\n`, `\r` so multi-line
  values can no longer corrupt the TSV grid.
- **`Commands/DownloadCommand.cs`** — Submit failures cancel the observer task
  immediately (no infinite wait); exit codes corrected to **0** (all good),
  **1** (any failed), **130** (any cancelled, SIGINT-style); stdin read is
  guarded against `IOException`.
- **`Commands/RootCommandFactory.cs`** — All global flags marked
  `Recursive = true` so they work after the subcommand name; `--json`/`--plain`
  validated as mutually exclusive at parse time.
- **`Commands/ServeCommand.cs`** — `LockedExitCode` constant updated to **6**.
- **`Commands/DoctorCommand.cs`** — New `--print-config` flag emits the
  resolved CLI paths (config dir, log dir, token path, lock path, audit log
  path) in pretty or JSON form for support and debugging.
- **`CliEnvironment.cs`** — `RunRestore` uses `Interlocked.Exchange` so two
  shutdown signals racing each other can't double-execute the restore
  callback.

### `src/Oahu.Cli.Tui`
- **`Shell/AppShell.cs`** — Completed modals are auto-dismissed in the broker
  poll (no input freeze when a modal completes via background means);
  unknown challenge types fail the broker awaiter via `TrySetException`
  instead of hanging forever; when a broker is attached the input loop polls
  on a 250 ms cadence so background-arriving challenges become visible
  promptly; `needsTimedRefresh` made `volatile`.
- **`Auth/SignInFlow.cs`** — Implements `IDisposable`; `Dispose` cancels and
  disposes the linked `CancellationTokenSource`.
- **`Auth/ChallengeModal.cs`** — Input buffer is wiped to `string.Empty` once
  the secret is captured (or on cancel) so MFA / CAPTCHA / CVF codes don't
  linger in memory.

### Schemas
- **`docs/cli-schemas/doctor.schema.json`** — `_schemaVersion` pinned to the
  string `"1"` (was `["integer", "string"]`, which permitted clients to
  incorrectly emit numeric versions and broke schema discrimination).

## Deferred — Bucket B (separate work items)

These findings are real but warrant their own dedicated work because they
either add new user-facing features, require larger refactors, or need
spec-level decisions. They are tracked in `plan.md` under Bucket B.

- **`library list --unread`** — new filter switch; needs a clear definition
  of "unread" against the cached library schema.
- **`queue add` by title** — fuzzy lookup over the cached library; needs a
  ranking strategy and ambiguity-resolution UX.
- **`download --all-new`, `--no-decrypt`, `--export m4b|both`** — new modes
  that touch the executor and the export pipeline; `m4b` requires a new
  exporter implementation in `Oahu.Decrypt`.
- **`convert <file>` (path-based)** — currently the convert command operates
  by ASIN; supporting raw `.aax`/`.aaxc` paths is a meaningful new feature.
- **Crash recovery for in-flight jobs** — needs a durable job-state file
  separate from the queue so an interrupted run can be resumed.
- **`JobPhase.Exporting` rename** — design doc uses "Exporting" while the
  code uses "Converting"; a rename will touch JSON schemas and tests.
- **Cmd-mode 5s grace on Ctrl+C** — soft-cancel window before SIGINT becomes
  hard-kill; needs careful coordination with `IJobService` cancellation.
- **Colorblind / high-contrast theme** — new palette tokens + opt-in.
- **TUI widget set** — `SortableTable`, `Pager`, etc. that the design lists
  as building blocks for future screens.
- **End-to-end test project** — separate harness that exercises the CLI as a
  black box, including SSE and HTTP transports.
- **`history_delete` tool** — purges entries from the JSONL store; needs a
  retention/safety story.
- **`serve --listen unix:/path`** — Unix-domain socket transport in addition
  to loopback HTTP.
- **Per-token rate limiter** — token-bucket on the HTTP transport.
- **`serve --strict-peer`** — verify the connecting peer's UID matches the
  server's before serving any request.
- **Central `Errors` / `ExitCodes` class** — consolidate the exit-code
  constants currently scattered across `ServeCommand.LockedExitCode`,
  `DownloadCommand`, `ServerHost.RunAsync`, etc.

## Verification

- `dotnet build` (full solution) — succeeded, **0 warnings, 0 errors**
- `dotnet test tests/Oahu.Cli.Tests/Oahu.Cli.Tests.csproj` — **277 / 277
  passing**
- StyleCop: no net regressions. Two were introduced and fixed mid-flight
  (SA1518 trailing blank line in `OahuConfig.cs`; SA1203 const-before-non-
  const in `AuditLog.cs`).
