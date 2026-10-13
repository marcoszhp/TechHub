using System.Windows;
using TechHub.Core.Models;

namespace TechHub.App.Services;

public interface IActionConfirmationService
{
    Task<bool> ConfirmAsync(ActionPreview preview);
}

public sealed class MessageBoxActionConfirmationService : IActionConfirmationService
{
    public Task<bool> ConfirmAsync(ActionPreview preview)
    {
        var resources = preview.AffectedResources.Count == 0
            ? "Nenhum recurso informado."
            : string.Join(Environment.NewLine, preview.AffectedResources.Select(item => $"• {item}"));
        var message = $"{preview.Description}{Environment.NewLine}{Environment.NewLine}Recursos afetados:{Environment.NewLine}{resources}{Environment.NewLine}{Environment.NewLine}Deseja continuar?";
        var result = MessageBox.Show(message, preview.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
