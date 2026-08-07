using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Creates (without overwriting) and expands the daily / weekly / monthly Markdown templates.</summary>
public interface ITemplateService
{
    Task EnsureDefaultTemplatesAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<string> ReadExpandedTemplateAsync(PeriodicNoteType type, DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
}
