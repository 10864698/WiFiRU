namespace WiFiRU
{
    internal class WifiSignal
    {
        public string Bssid { get; internal set; }
        public double NetworkRssiInDecibelMilliwatts { get; internal set; }
        // This is for the GUI only
        public string Ssid { get; internal set; }
    }
}