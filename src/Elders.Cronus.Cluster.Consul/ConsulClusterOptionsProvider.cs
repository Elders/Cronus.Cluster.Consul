using Elders.Cronus.Hosting;
using Microsoft.Extensions.Configuration;

namespace Elders.Cronus.Persistence.Cassandra
{
    public class ConsulClusterOptionsProvider : CronusOptionsProviderBase<ConsulClusterOptions>
    {
        public ConsulClusterOptionsProvider(IConfiguration configuration) : base(configuration) { }

        public override void Configure(ConsulClusterOptions options)
        {
            options.Address = configuration["cronus:cluster:consul:address"];
            options.Address = configuration["cronus:cluster:consul:port"];
        }
    }
}
