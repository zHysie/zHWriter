namespace zHWriter.Core.Models;

/// <summary>A loaded daily journal document and its authoritative date/path.</summary>
public sealed record JournalDocument(DateOnly Date, string Path, string Content, DateTime LastWriteTimeUtc, bool WasCreated);
