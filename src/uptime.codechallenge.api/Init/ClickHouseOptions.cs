namespace uptime.codechallenge.api.Init
{
    public class ClickHouseOptions
    {
        // This constant is used to bind the correct section from appsettings.json
        public const string SectionName = "ClickHouse";

        public string Host { get; set; } = "localhost";
        public ushort Port { get; set; } = 8123;
        public string Database { get; set; } = "default";
        public string Username { get; set; } = "default";
        public string Password { get; set; } = string.Empty;
        public bool UseCompression { get; set; } = true;
    }
}
