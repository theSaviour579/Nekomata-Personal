using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Authority;
using Nekomata.Models.Guardian;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Services;

/// <summary>
/// Surfaces Guardian's AutoExecute-tier actions on the desktop orb and voice (see
/// MainViewModel.AcknowledgeAutoExecutedActionAsync), so they are never silent except in
/// the audit log. Resolves MainViewModel lazily through the service provider at
/// notification time rather than taking it as a constructor dependency, so construction
/// order never matters and there is no risk of a circular dependency with the engines that
/// depend on this notifier.
/// </summary>
public sealed class DesktopGuardianAutoExecutionNotifier(IServiceProvider services) : IGuardianAutoExecutionNotifier
{
    public Task NotifyAutoExecutedAsync(GuardianActionAuthorityDecision decision, GuardianActionAuthorityRequest request)
    {
        var summary = string.IsNullOrWhiteSpace(request.Title) ? request.ActionType : request.Title;
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var viewModel = services.GetRequiredService<MainViewModel>();
                await viewModel.AcknowledgeAutoExecutedActionAsync(summary);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-execution acknowledgement failed safely: {ex}");
            }
        });
        return Task.CompletedTask;
    }
}
