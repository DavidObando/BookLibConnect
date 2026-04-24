using System;
using System.Collections.Generic;

namespace Oahu.Cli.App.Doctor;

/// <summary>Severity of an individual <see cref="DoctorCheck"/>.</summary>
public enum DoctorSeverity
{
    Ok,
    Warning,
    Error,
}

/// <summary>One environment check executed by <see cref="IDoctorService"/>.</summary>
public sealed record DoctorCheck(
    string Id,
    string Title,
    DoctorSeverity Severity,
    string Message,
    string? Hint = null);

/// <summary>Aggregate result returned by <see cref="IDoctorService.RunAsync"/>.</summary>
public sealed class DoctorReport
{
    public DoctorReport(IReadOnlyList<DoctorCheck> checks)
    {
        Checks = checks ?? throw new ArgumentNullException(nameof(checks));
    }

    public IReadOnlyList<DoctorCheck> Checks { get; }

    public bool HasErrors
    {
        get
        {
            foreach (var c in Checks)
            {
                if (c.Severity == DoctorSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public bool HasWarnings
    {
        get
        {
            foreach (var c in Checks)
            {
                if (c.Severity == DoctorSeverity.Warning)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
