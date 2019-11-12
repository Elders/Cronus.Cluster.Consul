using Microsoft.Extensions.DependencyInjection;
using System;

namespace Elders.Cronus.Cluster.Consul
{
    public static partial class ConsulServiceCollectionExtensions
    {
        public static IServiceCollection AddConsul(this IServiceCollection services)
        {
            services.AddHttpClient<CronusJobRunner>("cronus", (provider, client) =>
            {
                var builder = new UriBuilder("10.0.75.2");
                builder.Port = 8500;

                client.BaseAddress = builder.Uri;

                //var authorization = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{gateway.ApiClient.ApiUsername}:{gateway.ApiClient.ApiPassword}"));
                //client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authorization);
            });


            //services.AddTransient<CronusJobRunner>();

            return services;
        }
    }
}
