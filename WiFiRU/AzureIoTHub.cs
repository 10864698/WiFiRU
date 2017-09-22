using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
//using Microsoft.Devices.Tpm;

class AzureIoTHub
{
    private static void CreateClient()
    {
        if (deviceClient == null)
        {
            // create Azure IoT Hub client from embedded connection string
            deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
        }
    }

    static DeviceClient deviceClient = null;

    //
    // Note: this connection string is specific to the device "WiFiRU1". To configure other devices,
    // see information on iothub-explorer at http://aka.ms/iothubgetstartedVSCS
    //
    const string deviceConnectionString = "HostName=WiFiCU.azure-devices.net;DeviceId=WiFiRU1;SharedAccessKey=sWRTQby/As261Edbg0XrV96kJut6n0J44Fow0MTUltE=";


    //
    // To monitor messages sent to device "kraaa" use iothub-explorer as follows:
    //    iothub-explorer monitor-events --login HostName=WiFiCU.azure-devices.net;SharedAccessKeyName=service;SharedAccessKey=eHLP8o3Lw8xrrHpye3ht5eMjTlVbVxYhuSWQVJMf0nU= "WiFiRU1"
    //

    // Refer to http://aka.ms/azure-iot-hub-vs-cs-2017-wiki for more information on Connected Service for Azure IoT Hub

    public static async Task SendDeviceToCloudMessageAsync(string deviceData)
    {
        CreateClient();
        //TpmDevice myDevice = new TpmDevice(0); // Use logical device 0 on the TPM by default

        //string hubUri = myDevice.GetHostName();
        //string deviceId = myDevice.GetDeviceId();
        //string sasToken = myDevice.GetSASToken();

        //var deviceClient = DeviceClient.Create(hubUri, AuthenticationMethodFactory.CreateAuthenticationWithToken(deviceId, sasToken), TransportType.Mqtt);

        var deviceMessage = new Message(Encoding.ASCII.GetBytes(deviceData));

        await deviceClient.SendEventAsync(deviceMessage);
    }

    public static async Task<string> ReceiveCloudToDeviceMessageAsync()
    {
        CreateClient();

        while (true)
        {
            var receivedMessage = await deviceClient.ReceiveAsync();

            if (receivedMessage != null)
            {
                var messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                await deviceClient.CompleteAsync(receivedMessage);
                return messageData;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
