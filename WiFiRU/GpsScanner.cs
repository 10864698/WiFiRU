namespace WiFiRU
{
    internal class GpsScanner
    {
        public string LocationStatus { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DateTime { get; set; }

        public GpsScanner()
        {
            LocationStatus = "A";
            Latitude = 0.0;
            Longitude = 0.0;
            DateTime = System.DateTimeOffset.Now.ToUniversalTime().ToString("u");
        }

    }
}