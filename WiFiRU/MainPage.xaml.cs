using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Data.Sqlite;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Devices.WiFi;
using Windows.UI.Popups;
using System.Text.RegularExpressions;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WiFiRU
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public /*sealed*/ partial class MainPage : Page, INotifyPropertyChanged
    {
        private WifiAdapterScanner wifiScanner;
        private GpsScanner gpsScanner;

        public MainPage()
        {
            InitializeComponent();

            wifiScanner = new WifiAdapterScanner();

            DataContext = wifiScanner;
        }

        private async void PageLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeScanner();
        }

        private async Task InitializeScanner()
        {
            await wifiScanner.InitializeScanner();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private async void ScanButtonClick(object sender, RoutedEventArgs e)
        {
            ButtonScan.IsEnabled = false;

            try
            {
                await RunWifiScan();
            }
            catch (Exception ex)
            {
                MessageDialog md = new MessageDialog(ex.Message);
                await md.ShowAsync();
            }

            ButtonScan.IsEnabled = true;
        }

        private async Task RunWifiScan()
        {
            await wifiScanner.ScanForNetworks();
            WiFiNetworkReport report = wifiScanner.WifiAdapter.NetworkReport;

            gpsScanner = new GpsScanner
            {
                Latitude = -33.89736,
                Longitude = 151.1547,
                TimeStamp = DateTimeOffset.Now
            };
            var venueIdentification = GenerateVenueIdentification(gpsScanner.Latitude, gpsScanner.Longitude);

            foreach (var availableNetwork in report.AvailableNetworks)
            {
                WifiSignal wifiSignal = new WifiSignal()
                {
                    Bssid = availableNetwork.Bssid,
                    NetworkRssiInDecibelMilliwatts = availableNetwork.NetworkRssiInDecibelMilliwatts,
                    Ssid = availableNetwork.Ssid
                };

                AddWifiScanResultsToWifiScannerDatabase(wifiSignal, gpsScanner, venueIdentification);
            }
        }

        private void AddWifiScanResultsToWifiScannerDatabase(WifiSignal wifiSignal, GpsScanner gpsScanner, string tableName)
        {
            using (SqliteConnection database = new SqliteConnection("Filename = WiFiScanner.db"))
            {
                database.Open();

                //create if table doesn't exist
                CreateVenueTableInWifiScannerDatabaseIfNotExists(tableName);

                //check if VenueName / Bssid / Ssid exists already
                using (SqliteCommand sqlCheckExistingWifiSignalCommand = new SqliteCommand())
                {
                    sqlCheckExistingWifiSignalCommand.Connection = database;
                    sqlCheckExistingWifiSignalCommand.CommandText = "SELECT count(*) FROM " + tableName + " " +
                        "WHERE Bssid = @Bssid " +
                        "AND Ssid = @Ssid";
                    sqlCheckExistingWifiSignalCommand.Parameters.AddWithValue("@Bssid", wifiSignal.Bssid); //string TEXT
                    sqlCheckExistingWifiSignalCommand.Parameters.AddWithValue("@Ssid", wifiSignal.Ssid); //string TEXT
                    int count = Convert.ToInt32(sqlCheckExistingWifiSignalCommand.ExecuteScalar()); //check for existing record
                    if (count == 0)
                    {
                        using (SqliteCommand insertCommand = new SqliteCommand())
                        {
                            insertCommand.Connection = database;
                            insertCommand.CommandText = "INSERT INTO " + tableName + " " +
                                "(" +
                                "Bssid, " +
                                "NetworkRssiInDecibelMilliwatts, " +
                                "Ssid, " +
                                "TimeStamp" +
                                ")" +
                                "VALUES " +
                                "(" +
                                "@Bssid," +
                                "@NetworkRssiInDecibelMilliwatts," +
                                "@Ssid," +
                                "@TimeStamp" +
                                ")";
                            insertCommand.Parameters.AddWithValue("@Bssid", wifiSignal.Bssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@NetworkRssiInDecibelMilliwatts", wifiSignal.NetworkRssiInDecibelMilliwatts); //double REAL
                            insertCommand.Parameters.AddWithValue("@Ssid", wifiSignal.Ssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@TimeStamp", gpsScanner.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss.fff")); //string TEXT

                            try
                            {
                                insertCommand.ExecuteNonQuery();
                            }
                            catch (SqliteException e)
                            {
                                throw new Exception("SQL table INSERT not performed" + count.ToString());
                            }
                        }
                    }
                    database.Close(); database.Dispose();
                }

                VenueIdTextBox.Text = tableName;
                OutputTextBlock.ItemsSource = ReadWifiScannerDatabase(tableName);
            }
        }

        private void CreateVenueTableInWifiScannerDatabaseIfNotExists(string tableName)
        {
            using (SqliteConnection database = new SqliteConnection("Filename = WiFiScanner.db"))
            {
                try
                {
                    database.Open();
                }
                catch (SqliteException e)
                {
                    throw new Exception("SQL database not opened.");
                }

                String sqlCreateTableCommand = "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                    "Bssid TEXT," +
                    "NetworkRssiInDecibelMilliwatts REAL," +
                    "Ssid TEXT," +
                    "TimeStamp TEXT " +
                    ")";

                SqliteCommand createTable = new SqliteCommand(sqlCreateTableCommand, database);
                try
                {
                    createTable.ExecuteNonQuery();
                }
                catch (SqliteException e)
                {
                    throw new Exception("SQL table " + tableName + " not created.");
                }
                database.Close(); database.Dispose();
            }

            VenueIdTextBox.Text = tableName;
            OutputTextBlock.ItemsSource = ReadWifiScannerDatabase(tableName);
        }

        private List<String> ReadWifiScannerDatabase(string tableName)
        {
            List<String> entries = new List<string>();

            using (SqliteConnection database = new SqliteConnection("Filename = WiFiScanner.db"))
            {
                database.Open();

                SqliteCommand sqlSelectCommand = new SqliteCommand(
                    "SELECT Ssid, Bssid, NetworkRssiInDecibelMilliwatts, TimeStamp " +
                    "FROM " + tableName + " " +
                    "ORDER BY NetworkRssiInDecibelMilliwatts DESC", database);
                SqliteDataReader query;

                try
                {
                    query = sqlSelectCommand.ExecuteReader();
                }
                catch (SqliteException e)
                {
                    throw new Exception("SQL database no entries in table." + tableName);
                    //return entries;
                }

                while (query.Read())
                {
                    entries.Add("[" + query.GetString(0) + "] "
                        + " [MAC " + query.GetString(1) + "] "
                        + query.GetString(2) + " dBm "
                        + query.GetString(3));
                }

                database.Close(); database.Dispose();
            }
            return entries;
        }

        private string GenerateVenueIdentification(double latitude, double longitude)
        {
            var latitudeString = latitude.ToString();
            var longitudeString = longitude.ToString();

            if (latitude < 0)
            {
                latitudeString = "LAT" + latitudeString + "S";
            }
            else { latitudeString = "LAT" + latitudeString + "N"; }
            latitudeString = latitudeString.Replace('.', 'D');

            if (longitude < 0)
            {
                longitudeString = "LON" + longitudeString + "W";
            }
            else { longitudeString = "LON" + longitudeString + "E"; }
            longitudeString = longitudeString.Replace('.', 'D');

            Regex rgx = new Regex("[^a-zA-Z0-9]");
            return rgx.Replace((latitudeString + longitudeString), "");
        }
    }
}
