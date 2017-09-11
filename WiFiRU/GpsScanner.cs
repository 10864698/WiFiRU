namespace WiFiRU
{
    internal class GpsScanner
    {
        public string LocationStatus { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public string DateTime { get; set; }

        public GpsScanner()
        {
            LocationStatus = "A";
            Latitude = "LAT12345N6789";
            Longitude = "LON1234E5678";
            DateTime = System.DateTimeOffset.Now.ToUniversalTime().ToString("u");
        }

    }
}