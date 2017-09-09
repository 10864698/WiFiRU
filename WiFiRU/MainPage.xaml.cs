using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Data.Sqlite;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Devices.WiFi;
using Windows.UI.Popups;

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
        private string venueIdentification;

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

        private void VenueIdTextChanged(object sender, TextChangedEventArgs e)
        {
            if (venueIdTextBox.Text == "")
            {
                VenueIdentification = "No Name Entered";
            }
            else
            {
                VenueIdentification = venueIdTextBox.Text;
            }
        }

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string VenueIdentification
        {
            get
            {
                if ((venueIdentification == "") | (venueIdentification == null))
                    return "No ID generated";
                else
                    return venueIdentification;
            }
            set
            {
                if ((venueIdTextBox.Text == "") | (venueIdTextBox.Text == null))
                {
                    venueIdentification = "No ID generated";
                    OnPropertyChanged("venueIdTextBox");
                }
                else
                {
                    venueIdentification = venueIdTextBox.Text;
                    OnPropertyChanged("venueIdTextBox");
                }
            }

        }

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

            gpsScanner = new GpsScanner();
            gpsScanner.Latitude = -33.89736;
            gpsScanner.Longitude = 151.1547;
            gpsScanner.TimeStamp = DateTimeOffset.Now;
            VenueIdentification = "Test";

            foreach (var availableNetwork in report.AvailableNetworks)
            {
                WifiSignal wifiSignal = new WifiSignal()
                {
                    Bssid = availableNetwork.Bssid,
                    NetworkRssiInDecibelMilliwatts = availableNetwork.NetworkRssiInDecibelMilliwatts,
                    Ssid = availableNetwork.Ssid,
                    Uptime = availableNetwork.Uptime
                };

                AddWifiScanResultsToWifiScannerDatabase(wifiSignal, gpsScanner);
            }
        }

        private void AddWifiScanResultsToWifiScannerDatabase(WifiSignal wifiSignal, GpsScanner gpsScanner)
        {
            using (SqliteConnection database = new SqliteConnection("Filename = WiFiScanner.db"))
            {
                database.Open();

                //create if table doesn't exist
                CreateVenueTableInWifiScannerDatabaseIfNotExists(RemoveWhiteSpace(VenueIdentification));

                //check if VenueName / Bssid / Ssid exists already
                using (SqliteCommand sqlCheckExistingWifiSignalCommand = new SqliteCommand())
                {
                    sqlCheckExistingWifiSignalCommand.Connection = database;
                    sqlCheckExistingWifiSignalCommand.CommandText = "SELECT count(*) FROM " + RemoveWhiteSpace(VenueIdentification) + " " +
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
                            insertCommand.CommandText = "INSERT INTO " + RemoveWhiteSpace(VenueIdentification) + " " +
                                "(" +
                                "Bssid, " +
                                "NetworkRssiInDecibelMilliwatts, " +
                                "Ssid, " +
                                "Uptime, " +
                                "TimeStamp" +
                                ")" +
                                "VALUES " +
                                "(" +
                                "@Bssid," +
                                "@NetworkRssiInDecibelMilliwatts," +
                                "@Ssid," +
                                "@Uptime," +
                                "@TimeStamp" +
                                ")";
                            insertCommand.Parameters.AddWithValue("@Bssid", wifiSignal.Bssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@NetworkRssiInDecibelMilliwatts", wifiSignal.NetworkRssiInDecibelMilliwatts); //double REAL
                            insertCommand.Parameters.AddWithValue("@Ssid", wifiSignal.Ssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@Uptime", wifiSignal.Uptime.Ticks); //long INTEGER
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

                Output.ItemsSource = ReadWifiScannerDatabase;
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

                String sqlCreateTableCommand = "CREATE TABLE IF NOT EXISTS " + RemoveWhiteSpace(tableName) + " (" +
                    "Bssid TEXT," +
                    "NetworkRssiInDecibelMilliwatts REAL," +
                    "Ssid TEXT," +
                    "Uptime INTEGER," +
                    "TimeStamp TEXT " +
                    ")";

                SqliteCommand createTable = new SqliteCommand(sqlCreateTableCommand, database);
                try
                {
                    createTable.ExecuteNonQuery();
                }
                catch (SqliteException e)
                {
                    throw new Exception("SQL table " + RemoveWhiteSpace(tableName) + " not created.");
                }
                database.Close(); database.Dispose();
            }

            Output.ItemsSource = ReadWifiScannerDatabase;
        }

        private string RemoveWhiteSpace(string input)
        {
            int j = 0, inputlen = input.Length;
            char[] newarr = new char[inputlen];

            for (int i = 0; i < inputlen; ++i)
            {
                char tmp = input[i];

                if (!char.IsWhiteSpace(tmp))
                {
                    newarr[j] = tmp;
                    ++j;
                }
            }
            return new String(newarr, 0, j);
        }

        private List<String> ReadWifiScannerDatabase
        {
            get
            {
                List<String> entries = new List<string>();

                using (SqliteConnection database = new SqliteConnection("Filename = WiFiScanner.db"))
                {
                    database.Open();

                    SqliteCommand sqlSelectCommand = new SqliteCommand(
                        "SELECT Ssid, Bssid, NetworkRssiInDecibelMilliwatts, TimeStamp, Uptime " +
                        "FROM " + RemoveWhiteSpace(VenueIdentification) + " " +
                        "ORDER BY NetworkRssiInDecibelMilliwatts DESC, Uptime DESC", database);
                    SqliteDataReader query;

                    try
                    {
                        query = sqlSelectCommand.ExecuteReader();
                    }
                    catch (SqliteException e)
                    {
                        throw new Exception("SQL database no entries in table." + RemoveWhiteSpace(VenueIdentification));
                        //return entries;
                    }

                    while (query.Read())
                    {
                        TimeSpan interval = TimeSpan.FromTicks(query.GetInt64(4));
                        string uptime = interval.ToString("%d") + " day(s) " + interval.ToString(@"hh\:mm");

                        entries.Add("[" + query.GetString(0) + "] "
                            + " [MAC " + query.GetString(1) + "] "
                            + query.GetString(2) + " dBm "
                            + "Uptime:" + uptime + " "
                            + query.GetFloat(3).ToString());
                    }

                    database.Close(); database.Dispose();
                }
                return entries;
            }
        }
    }
}