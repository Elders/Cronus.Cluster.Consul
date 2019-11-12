namespace Elders.Cronus.Cluster.Consul.Internal
{
    internal class KVResponse
    {
        public KVResponse()
        {
            Key = string.Empty;
            Session = string.Empty;
            Value = string.Empty;
        }

        public string Key { get; set; }

        public string Session { get; set; }

        public string Value { get; set; }
    }
}
