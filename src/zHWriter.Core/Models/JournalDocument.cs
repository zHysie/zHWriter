namespace zHWriter.Core.Models;

/// <summary>A loaded periodic note document and its authoritative period/date/path.</summary>
public sealed record JournalDocument(PeriodicNoteType Type, DateOnly Date, string Path, string Content, DateTime LastWriteTimeUtc, bool WasCreated);
