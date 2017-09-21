using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Data.Sqlite;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Devices.WiFi;
using Windows.UI.Popups;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Collections.ObjectModel;
using System.Threading;
using Windows.Devices.Enumeration;

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
        private static string databaseName = "WiFiScanner.db";
        private static string tableName = "venueData";

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
                    StatusTextBox.Text = "Waiting for GPS data...";

                    // Create cancellation token object to close I/O operations when closing the device
                    ReadCancellationTokenSource = new CancellationTokenSource();

                    Listen();
                }
                catch (Exception ex)
                {
                    StatusTextBox.Text = ex.Message;
                }
            }
            catch (Exception ex)
            {
                StatusTextBox.Text = ex.Message;
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
                StatusTextBox.Text = ex.Message;
            }
            catch (Exception ex)
            {
                StatusTextBox.Text = ex.Message;
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
        /// ReadAsync task that waits on data and reads asynchronously from the serial device InputStream
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

                    // clear to first RMC message
                    gpsDataReaderStream = gpsDataReaderStream.Remove(0, gpsDataReaderStream.IndexOf("GPRMC"));
                    // get the GPRMC string
                    string gprmcMessage = gpsDataReaderStream.Substring(gpsDataReaderStream.IndexOf("GPRMC"), (gpsDataReaderStream.IndexOf("*") + 3) - gpsDataReaderStream.IndexOf("GPRMC"));

                    // Calculate checksum
                    int checksum = 0;
                    for (int i = 0; i < (gprmcMessage.Length - 3); i++)
                    {
                        checksum ^= Convert.ToByte(gprmcMessage[i]);
                    }
                    string strChecksum = checksum.ToString("X2");

                    string[] gprmcFields = gprmcMessage.Split(',', '*');

                    if (!(gprmcFields[2] == "A"))
                    {
                        StatusTextBox.Text = "No GPS fix";
                    }
                    if (!(strChecksum == gprmcFields[gprmcFields.Length - 1]))
                    {
                        StatusTextBox.Text = "Invalid checksum";
                    }
                    else
                    {
                        gpsScanner.RmcMessage = gprmcMessage;

                        StatusTextBox.Text = gprmcFields[3] + gprmcFields[4] + " " + gprmcFields[5] + gprmcFields[6] + " " + gprmcFields[9] + " " + gprmcFields[1];
                    }
                }
            }
        }

        /// <summary>
        /// CancelReadTask uses the ReadCancellationTokenSource to cancel read operations
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

        /// <summary>
        /// ScanButtonClick will be replaced by the SU status message (via Bluetooth door switch)
        /// </summary>
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

        /// <summary>
        /// RunWifiScan gathers the Wifi data, gets the GPs data and calls the add-to-database method
        /// </summary>
        /// <returns></returns>
        private async Task RunWifiScan()
        {
            await wifiScanner.ScanForNetworks();
            WiFiNetworkReport report = wifiScanner.WifiAdapter.NetworkReport;

            foreach (var availableNetwork in report.AvailableNetworks)
            {
                WifiSignal wifiSignal = new WifiSignal()
                {
                    Bssid = availableNetwork.Bssid,
                    NetworkRssiInDecibelMilliwatts = availableNetwork.NetworkRssiInDecibelMilliwatts,
                    // This is for the GUI only
                    Ssid = availableNetwork.Ssid
                };

                AddWifiScanResultsToWifiScannerDatabase(wifiSignal, gpsScanner, databaseName, tableName);
            }
        }

        /// <summary>
        /// AddWifiScanResultsToWifiScannerDatabase adds the Wifi and GPS data to the SQLite database
        /// </summary>
        /// <param name="wifiSignal"></param>
        /// <param name="gpsScanner"></param>
        /// <param name="databaseName"></param>
        /// <param name="tableName"></param>
        private void AddWifiScanResultsToWifiScannerDatabase(WifiSignal wifiSignal, GpsScanner gpsScanner, string databaseName, string tableName)
        {
            using (SqliteConnection database = new SqliteConnection("Filename = " + databaseName))
            {
                try
                {
                    database.Open();
                }
                catch (SqliteException ex)
                {
                    StatusTextBox.Text = ex.Message;
                    throw new Exception("SQL database not opened.");
                }

                // create if table doesn't exist
                SqliteCommand createTable = new SqliteCommand();
                createTable.Connection = database;
                createTable.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} (Rmc TEXT, Bssid TEXT, NetworkRssiInDecibelMilliwatts REAL, Ssid TEXT)";

                try
                {
                    createTable.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    StatusTextBox.Text = ex.Message;
                    throw new Exception("SQL table " + tableName + " not created.");
                }

                database.Close(); database.Dispose();
            }

            using (SqliteConnection database = new SqliteConnection("Filename = " + databaseName))
            {
                try
                {
                    database.Open();
                }
                catch (SqliteException ex)
                {
                    StatusTextBox.Text = ex.Message;
                    throw new Exception("SQL database not opened.");
                }

                // insert venue data
                SqliteCommand insertCommand = new SqliteCommand();
                insertCommand.Connection = database;
                insertCommand.CommandText = $"INSERT INTO {tableName} (Rmc, Bssid, NetworkRssiInDecibelMilliwatts, Ssid) VALUES (@Rmc, @Bssid, @NetworkRssiInDecibelMilliwatts, @Ssid)";
                insertCommand.Parameters.AddWithValue("@Rmc", gpsScanner.RmcMessage); //string TEXT
                insertCommand.Parameters.AddWithValue("@Bssid", wifiSignal.Bssid); //string TEXT
                insertCommand.Parameters.AddWithValue("@NetworkRssiInDecibelMilliwatts", wifiSignal.NetworkRssiInDecibelMilliwatts); //double REAL
                // This is for the GUI only
                insertCommand.Parameters.AddWithValue("@Ssid", wifiSignal.Ssid); //string TEXT

                try
                {
                    insertCommand.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    StatusTextBox.Text = ex.Message;
                }
                database.Close(); database.Dispose();
            }

            VenueIdTextBox.Text = tableName;
            OutputTextBlock.ItemsSource = ReadWifiScannerDatabase(databaseName, tableName);
        }

        /// <summary>
        /// ReadWifiScannerDatabase reads the data from the database
        /// </summary>
        /// <param name="databaseName"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        private List<String> ReadWifiScannerDatabase(string databaseName, string tableName)
        {
            List<String> entries = new List<string>();

            using (SqliteConnection database = new SqliteConnection("Filename = " + databaseName))
            {
                database.Open();

                SqliteCommand sqlSelectCommand = new SqliteCommand();
                sqlSelectCommand.Connection = database;
                sqlSelectCommand.CommandText = $"SELECT Rmc, Bssid, NetworkRssiInDecibelMilliwatts, Ssid FROM {tableName} ORDER BY NetworkRssiInDecibelMilliwatts DESC";

                SqliteDataReader query;

                try
                {
                    query = sqlSelectCommand.ExecuteReader();
                }
                catch (SqliteException ex)
                {
                    StatusTextBox.Text = ex.Message;
                    throw new Exception("SQL database no entries in table." + tableName);
                }

                while (query.Read())
                {
                    entries.Add("[MAC " + query.GetString(1) + "] " +
                        "[RSSI " + query.GetString(2) + " dBm] " +
                        query.GetString(3) + " " +
                        query.GetString(0));
                }

                database.Close(); database.Dispose();
            }
            return entries;
        }
    }
}
