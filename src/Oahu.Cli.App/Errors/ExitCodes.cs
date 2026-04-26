namespace Oahu.Cli.App.Errors;

/// <summary>Process exit codes used across the oahu-cli surface (per design §10).</summary>
/// <remarks>
/// Single source of truth — every command/host should return one of these constants
/// instead of magic numbers so that exit-code semantics stay consistent across the
/// codebase and so the design doc remains authoritative.
/// </remarks>
public static class ExitCodes
{
    /// <summary>0 — successful completion.</summary>
    public const int Success = 0;

    /// <summary>1 — generic failure (unexpected exception, unspecified runtime error).</summary>
    public const int GenericFailure = 1;

    /// <summary>2 — usage error (command-line parse or validation failure).</summary>
    public const int UsageError = 2;

    /// <summary>3 — authentication required or failed.</summary>
    public const int AuthError = 3;

    /// <summary>4 — Audible API error (HTTP failure from Oahu.Data, etc).</summary>
    public const int AudibleApiError = 4;

    /// <summary>5 — decryption / conversion error (Oahu.Decrypt failure).</summary>
    public const int DecryptError = 5;

    /// <summary>6 — single-instance lock contention (another oahu-cli already holds the user-data lock).</summary>
    public const int Locked = 6;

    /// <summary>130 — cancelled by user (SIGINT / Ctrl+C). Matches POSIX convention.</summary>
    public const int Cancelled = 130;
}
