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
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Collections.ObjectModel;
using System.Threading;
using Windows.Devices.Enumeration;
using System.Globalization;

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

        private SerialDevice serialPort = null;
        DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        public MainPage()
        {
            InitializeComponent();

            wifiScanner = new WifiAdapterScanner();
            listOfDevices = new ObservableCollection<DeviceInformation>();
            SelectFisrtSerialPort();

            DataContext = this;
        }

        private async void PageLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeScanner();
        }

        private async Task InitializeScanner()
        {
            await wifiScanner.InitializeScanner();
        }

        /// <summary>
        /// SelectDefaultPort
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void SelectFisrtSerialPort()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                DeviceInformationCollection dis = await DeviceInformation.FindAllAsync(aqs);

                try
                {
                    // Connect to first device
                    serialPort = await SerialDevice.FromIdAsync(dis[0].Id);
                    if (serialPort == null)
                    {
                        return;
                    }

                    serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                    serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                    serialPort.BaudRate = 9600;
                    serialPort.Parity = SerialParity.None;
                    serialPort.StopBits = SerialStopBitCount.One;
                    serialPort.DataBits = 8;
                    serialPort.Handshake = SerialHandshake.None;

                    // Set the RcvdText field to invoke the TextChanged callback
                    // The callback launches an async Read task to wait for data
                    ErrorMessageTextBox.Text = "Waiting for GPS data...";

                    // Create cancellation token object to close I/O operations when closing the device
                    ReadCancellationTokenSource = new CancellationTokenSource();

                    Listen();
                }
                catch (Exception ex)
                {
                    ErrorMessageTextBox.Text = ex.Message;
                }
            }
            catch (Exception ex)
            {
                ErrorMessageTextBox.Text = ex.Message;
            }
        }

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);
                    gpsScanner = new GpsScanner();

                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                ErrorMessageTextBox.Text = ex.Message;
            }
            catch (Exception ex)
            {
                ErrorMessageTextBox.Text = ex.Message;
            }
            finally
            {
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 2048;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            using (var childCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Create a task object to wait for data on the serialPort.InputStream
                loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(childCancellationTokenSource.Token);

                // Launch the task and wait
                UInt32 bytesRead = await loadAsyncTask;

                string gpsDataReaderStream = null;

                if (bytesRead > 0)
                {
                    gpsDataReaderStream = dataReaderObject.ReadString(bytesRead);

                    gpsDataReaderStream = gpsDataReaderStream.Remove(0, gpsDataReaderStream.IndexOf("GPRMC")); // clear to frst RMC message
                    string gprmcMessage = gpsDataReaderStream.Substring(gpsDataReaderStream.IndexOf("GPRMC"), (gpsDataReaderStream.IndexOf("*") + 3) - gpsDataReaderStream.IndexOf("GPRMC"));

                    // Validate checksum
                    int checksum = 0;
                    for (int i = 0; i < (gprmcMessage.Length -3); i++)
                    {
                        checksum ^= Convert.ToByte(gprmcMessage[i]);
                    }
                    string strChecksum = checksum.ToString("X2");

                    string[] gprmcField = gprmcMessage.Split(',','*');

                    gpsScanner.LocationStatus = gprmcField[2];

                    if (!(gprmcField[2] == "A"))
                    {
                        ErrorMessageTextBox.Text = "No GPS fix";
                    }
                    if(!(strChecksum == gprmcField[gprmcField.Length - 1]))
                    {
                        ErrorMessageTextBox.Text = "Invalid checksum";
                    }
                    else
                    {
                        gpsScanner.Latitude = gprmcField[3] + gprmcField[4];
                        gpsScanner.Longitude = gprmcField[5] + gprmcField[6];
                        gpsScanner.DateTime = ParseDateTime(gprmcField[9], gprmcField[1]);

                        ErrorMessageTextBox.Text = gprmcField[3] + gprmcField[4] + " " + gprmcField[5] + gprmcField[6] + " " + gprmcField[9] + " " + gprmcField[1];
                    }
                }
            }
        }

        private string ParseDateTime(string date, string time)
        {
            return DateTime.ParseExact(date + time, "ddMMyyHHmmss.fff", CultureInfo.InvariantCulture).ToString("u");
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
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

            string venueIdentification = GenerateVenueIdentification(gpsScanner.Latitude, gpsScanner.Longitude);

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
                                "TimeStamp " +
                                ") " +
                                "VALUES " +
                                "(" +
                                "@Bssid, " +
                                "@NetworkRssiInDecibelMilliwatts, " +
                                "@Ssid, " +
                                "@TimeStamp " +
                                ")";
                            insertCommand.Parameters.AddWithValue("@Bssid", wifiSignal.Bssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@NetworkRssiInDecibelMilliwatts", wifiSignal.NetworkRssiInDecibelMilliwatts); //double REAL
                            insertCommand.Parameters.AddWithValue("@Ssid", wifiSignal.Ssid); //string TEXT
                            insertCommand.Parameters.AddWithValue("@TimeStamp", gpsScanner.DateTime); //string TEXT

                            try
                            {
                                insertCommand.ExecuteNonQuery();
                            }
                            catch (SqliteException ex)
                            {
                                ErrorMessageTextBox.Text = ex.Message;
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
                catch (SqliteException ex)
                {
                    ErrorMessageTextBox.Text = ex.Message;
                    throw new Exception("SQL database not opened.");
                }

                String sqlCreateTableCommand = "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                    "Bssid TEXT, " +
                    "NetworkRssiInDecibelMilliwatts REAL, " +
                    "Ssid TEXT, " +
                    "TimeStamp TEXT " +
                    ")";

                SqliteCommand createTable = new SqliteCommand(sqlCreateTableCommand, database);
                try
                {
                    createTable.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    ErrorMessageTextBox.Text = ex.Message;
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
                catch (SqliteException ex)
                {
                    ErrorMessageTextBox.Text = ex.Message;
                    throw new Exception("SQL database no entries in table." + tableName);
                }

                while (query.Read())
                {
                    entries.Add(query.GetString(0)
                        + " [MAC " + query.GetString(1) + "] "
                        + " [RSSI " + query.GetString(2) + " dBm] "
                        + query.GetString(3));
                }

                database.Close(); database.Dispose();
            }
            return entries;
        }

        private string GenerateVenueIdentification(string gprmcLatitude, string gprmcLongitude)
        {
            gprmcLatitude = ParseLatitude(gprmcLatitude);
            gprmcLongitude = ParseLongitude(gprmcLongitude);

            Regex rgx = new Regex("[^a-zA-Z0-9]");

            return "LL" + rgx.Replace(gprmcLatitude + gprmcLongitude, "");
        }

        private string ParseLatitude(string coords)
        {
            // Latitude: DDMM.mmmm - e.g. 5321.5802 N
            if (!coords.EndsWith("N", StringComparison.OrdinalIgnoreCase) && !coords.EndsWith("S", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Latitude coordinate not found.");
            }

            if (coords.Length < 4)
            {
                throw new ArgumentException("Invalid latitude format.");
            }

            int dd = 0;
            try
            {
                dd = int.Parse(coords.Substring(0, 2));
                if (dd > 90)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch when (dd > 90)
            {
                throw new ArgumentOutOfRangeException("Degrees in latitude cannot exceed 90.");
            }
            catch
            {
                throw new ArgumentException("Invalid degrees format in latitude.");
            }

            double mm = 0.0D;
            try
            {
                string minutes = Regex.Match(coords.Substring(2), @"(\d+).(\d+)").Value;
                mm = double.Parse(minutes);
                if ((dd == 90 && mm > 0.0D) || mm >= 60.0D)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch when (dd == 90 && mm > 0.0D)
            {
                throw new ArgumentOutOfRangeException("Degrees in latitude cannot exceed 90.");
            }
            catch when (mm >= 60.0D)
            {
                throw new ArgumentOutOfRangeException("Minutes in latitude cannot exceed 60.");
            }
            catch
            {
                throw new ArgumentException("Invalid minutes format in latitude.");
            }

            double latitude = (dd + mm / 60);
            return latitude.ToString("F6") + coords.Substring(coords.Length - 1, 1);
        }

        private string ParseLongitude(string coords)
        {
            // Longitude: DDDMM.mmmm - e.g. 00630.3372 W
            if (!coords.EndsWith("W", StringComparison.OrdinalIgnoreCase) && !coords.EndsWith("E", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Longitude coordinate not found.");
            }

            if (coords.Length < 5)
            {
                throw new ArgumentException("Invalid longitude format.");
            }

            int ddd = 0;
            try
            {
                ddd = int.Parse(coords.Substring(0, 3));
                if (ddd > 180)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch when (ddd > 180)
            {
                throw new ArgumentOutOfRangeException("Degrees in longitude cannot exceed 180.");
            }
            catch
            {
                throw new ArgumentException("Invalid degrees format in longitude.");
            }

            double mm = 0.0D;
            try
            {
                string minutes = Regex.Match(coords.Substring(3), @"(\d+).(\d+)").Value;
                mm = double.Parse(minutes);
                if ((ddd == 180 && mm > 0.0D) || mm >= 60.0D)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            catch when (ddd == 180 && mm > 0.0D)
            {
                throw new ArgumentOutOfRangeException("Degrees in longitude cannot exceed 180.");
            }
            catch when (mm >= 60.0D)
            {
                throw new ArgumentOutOfRangeException("Minutes in longitude should be less than 60.");
            }
            catch
            {
                throw new ArgumentException("Invalid minutes format in longitude.");
            }

            double longitude = (ddd + mm / 60);
            return longitude.ToString("F6") + coords.Substring(coords.Length - 1, 1);
        }
    }
}
