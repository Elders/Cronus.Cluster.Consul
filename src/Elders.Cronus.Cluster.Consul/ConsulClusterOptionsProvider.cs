using Elders.Cronus.Hosting;
using Microsoft.Extensions.Configuration;
using System;

namespace Elders.Cronus.Persistence.Cassandra
{
    public class ConsulClusterOptionsProvider : CronusOptionsProviderBase<ConsulClusterOptions>
    {
        public ConsulClusterOptionsProvider(IConfiguration configuration) : base(configuration) { }

        public override void Configure(ConsulClusterOptions options)
        {
            options.Address = configuration.GetOptional("cronus:cluster:consul:address", "consul.local.com");
            options.Port = Int32.Parse(configuration.GetOptional("cronus:cluster:consul:port", "8500"));
        }
    }
}
