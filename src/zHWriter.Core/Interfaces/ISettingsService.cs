using zHWriter.Core.Models;

namespace zHWriter.Core.Interfaces;

/// <summary>Loads and saves settings outside the diary library.</summary>
public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
