namespace Elders.Cronus.Cluster.Consul.Internal
{
    internal class SessionRequest
    {
        public SessionRequest(string jobName)
        {
            Name = jobName;
        }

        public string Name { get; set; }

        public string TTL { get; set; } = "30s";

        public string LockDelay { get; set; } = "15s";

        public string Behavior { get; set; } = "release";
    }
}
