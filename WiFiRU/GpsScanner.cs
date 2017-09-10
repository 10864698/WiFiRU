using System;

namespace WiFiRU
{
    internal class GpsScanner
    {
        public double Accuracy { get; set; }
        public double Altitude { get; set; }
        public string LocationStatus { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTimeOffset TimeStamp { get; set; }

        public GpsScanner()
        { }
    }
}