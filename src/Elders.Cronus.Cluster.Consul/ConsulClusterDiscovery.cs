using System;
using System.Collections.Generic;
using System.Linq;
using Elders.Cronus.Cluster.Consul;
using Elders.Cronus.Cluster.Job;
using Elders.Cronus.Discoveries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elders.Cronus.Persistence.Cassandra
{
    public class ConsulClusterDiscovery : DiscoveryBase<ICronusJob<object>>
    {
        protected override DiscoveryResult<ICronusJob<object>> DiscoverFromAssemblies(DiscoveryContext context)
        {
            return new DiscoveryResult<ICronusJob<object>>(Enumerable.Empty<DiscoveredModel>(), AddServices);
        }

        private void AddServices(IServiceCollection services)
        {
            services.AddOptions<ConsulClusterOptions, ConsulClusterOptionsProvider>();

            services.AddHttpClient<ICronusJobRunner, CronusJobRunner>("cronus", (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ConsulClusterOptions>>().Value;
                var builder = new UriBuilder(options.Address);
                builder.Port = options.Port;

                client.BaseAddress = builder.Uri;

                //var authorization = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{gateway.ApiClient.ApiUsername}:{gateway.ApiClient.ApiPassword}"));
                //client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authorization);
            });
        }
    }
}
