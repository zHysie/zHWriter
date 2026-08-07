using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Validates settings and calculates periodic-note paths under the selected note-library root.</summary>
public interface IJournalPathService
{
    string GetNotePath(PeriodicNoteType type, DateOnly date, AppSettings settings);
    string GetTemplatePath(PeriodicNoteType type, AppSettings settings);
    string GetAttachmentDirectory(PeriodicNoteType type, DateOnly date, AppSettings settings);
    bool IsInsideDiaryRoot(string path, AppSettings settings);
    void Validate(AppSettings settings);
}
