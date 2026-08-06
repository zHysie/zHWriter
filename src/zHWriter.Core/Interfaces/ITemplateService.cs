using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Creates and expands the daily Markdown template.</summary>
public interface ITemplateService
{
    Task EnsureDefaultTemplateAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<string> ReadExpandedTemplateAsync(DateOnly date, AppSettings settings, CancellationToken cancellationToken = default);
}
