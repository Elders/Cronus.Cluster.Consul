using Microsoft.Extensions.Configuration;

namespace Elders.Cronus.Persistence.Cassandra
{
    public class ConsulClusterOptionsProvider : CronusOptionsProviderBase<ConsulClusterOptions>
    {
        public const string SettingKey = "cronus:cluster:consul";

        public ConsulClusterOptionsProvider(IConfiguration configuration) : base(configuration) { }

        public override void Configure(ConsulClusterOptions options)
        {
            configuration.GetSection(SettingKey).Bind(options);
        }
    }
}
