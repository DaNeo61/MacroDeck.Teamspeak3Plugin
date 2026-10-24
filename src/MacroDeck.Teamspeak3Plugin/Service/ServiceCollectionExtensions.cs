using MacroDeck.Teamspeak3Plugin.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTeamspeak3Connection(this IServiceCollection services)
        {
            // One instance serves both as the hosted service and as the dependency the integration subscribes to.
            services.AddSingleton<Teamspeak3ChannelState>();
            services.AddSingleton<CurrentChannelWidgetModel>();
            services.AddSingleton<Teamspeak3IconSet>();
            services.AddSingleton<Teamspeak3NotificationListener>();
            services.AddHostedService(provider => provider.GetRequiredService<Teamspeak3NotificationListener>());
            services.AddHostedService<Teamspeak3ConnectionMonitor>();
            return services;
        }
    }
}
