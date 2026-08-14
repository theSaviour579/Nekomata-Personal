using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Mail;

namespace Nekomata.Integrations.MicrosoftGraph.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoftGraph(
        this IServiceCollection services,
        MicrosoftGraphOptions options)
    {
        services.AddSingleton(options);

        services.AddSingleton<IMicrosoftAuthenticationService,
            MicrosoftAuthenticationService>();

        services.AddHttpClient<ICalendarService, CalendarService>(client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IEmailService, EmailService>(client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        return services;
    }
}