using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Validates journal settings and calculates paths under the selected diary root.</summary>
public interface IJournalPathService
{
    string GetJournalPath(DateOnly date, AppSettings settings);
    string GetTemplatePath(AppSettings settings);
    string GetAttachmentDirectory(DateOnly date, AppSettings settings);
    bool IsInsideDiaryRoot(string path, AppSettings settings);
    void Validate(AppSettings settings);
}
