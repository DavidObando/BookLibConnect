using System.Collections.Generic;

namespace Oahu.Cli.Output;

/// <summary>
/// Output abstraction shared by every command. Each handler chooses a default
/// shape and the writer implementation renders it for the active <see cref="OutputFormat"/>.
/// </summary>
public interface IOutputWriter
{
    OutputContext Context { get; }

    /// <summary>Writes a single resource as an object.</summary>
    void WriteResource(string resourceName, IReadOnlyDictionary<string, object?> data);

    /// <summary>Writes a homogeneous collection. Pretty/Plain renderers use <paramref name="columns"/>; JSON ignores it.</summary>
    void WriteCollection(
        string resourceName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<OutputColumn> columns);

    /// <summary>Writes a free-form message to the writer's stdout. Honours <see cref="OutputContext.Quiet"/>.</summary>
    void WriteMessage(string message);

    /// <summary>Writes an emphasised "this happened" line (✓ / etc.).</summary>
    void WriteSuccess(string message);
}

/// <summary>
/// Column metadata used by Pretty/Plain rendering.
/// </summary>
public sealed record OutputColumn(string Key, string Header)
{
    public OutputColumn(string key)
        : this(key, key)
    {
    }
}
