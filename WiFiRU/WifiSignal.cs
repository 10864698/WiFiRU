namespace WiFiRU
{
    internal class WifiSignal
    {
        public string Bssid { get; internal set; }
        public double NetworkRssiInDecibelMilliwatts { get; internal set; }
        public string Ssid { get; internal set; }
        public System.TimeSpan Uptime { get; internal set; }
    }
}