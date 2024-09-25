using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using Opc.UaFx;
using System.Net.Mime;
using System.Text;
using Microsoft.Identity.Client;
using Microsoft.Azure.Amqp.Framing;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;

// NONE           = 0 = 0000
// EMERGENCY STOP = 1 = 0001
// POWER FAILURE  = 2 = 0010
// SENSOR FAILURE = 4 = 0100
// UNKNOWN        = 8 = 1000
enum Errors
{
    EmergencyStop = 1,
    PowerFailure = 2,
    SensorFailue = 4,
    Unknown = 8
}

public class IoTDevice
{
    public static DeviceClient deviceClient;
    public static bool ifUpdate = false;
    public static bool created = false;

    public IoTDevice(DeviceClient deviceClient)
    {
        IoTDevice.deviceClient = deviceClient;
        Console.WriteLine("Connected to IoT");
    }

    #region Sending Messages
    public static async Task OneDeviceMagic(OpcClient opcClient, int deviceId)
    {
        var node = opcClient.BrowseNode(OpcObjectTypes.ObjectsFolder);

        #region telemetryValues

        int productionStatus = (int)opcClient.ReadNode($"ns=2;s=Device {deviceId}/ProductionStatus").Value;
        string workorderId = (string)opcClient.ReadNode($"ns=2;s=Device {deviceId}/WorkorderId").Value;
        long goodCount = (long)opcClient.ReadNode($"ns=2;s=Device {deviceId}/GoodCount").Value;
        long badCount = (long)opcClient.ReadNode($"ns=2;s=Device {deviceId}/BadCount").Value;
        double temperature = (double)opcClient.ReadNode($"ns=2;s=Device {deviceId}/Temperature").Value;
        int productionRate = (int)opcClient.ReadNode($"ns=2;s=Device {deviceId}/ProductionRate").Value;

        dynamic telemetryData = new
        {
            productionStatus = productionStatus,
            workorderId = workorderId,
            goodCount = goodCount,
            badCount = badCount,
            temperature = temperature,
            productionRate = productionRate
        };
        Console.WriteLine(telemetryData);
        await SendTelemetryData(deviceClient, telemetryData);

        #endregion telemetryValues

        await CheckIfUpdateTwin(opcClient);
    }
    public static async Task SendTelemetryData(DeviceClient client, dynamic machineData)
    {
        Console.WriteLine("Device sending telemetry data to IoT Hub...");

        var dataString = JsonConvert.SerializeObject(machineData);
        Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8"
        };

        await client.SendEventAsync(eventMessage);
    }

    #endregion Sending Messages

    #region Receiving Messages

    private static async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
    {
        Console.WriteLine($"\t{DateTime.Now}> C2D message callback - message received with Id={receivedMessage.MessageId}.");
        PrintMessage(receivedMessage);

        await deviceClient.CompleteAsync(receivedMessage);
        Console.WriteLine($"\t{DateTime.Now}> Completed C2D message with Id={receivedMessage.MessageId}.");

        receivedMessage.Dispose();
    }

    private static void PrintMessage(Message receivedMessage)
    {
        string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
        Console.WriteLine($"\t\tReceived message: {messageData}");

        int propCount = 0;
        foreach (var prop in receivedMessage.Properties)
        {
            Console.WriteLine($"\t\tProperty[{propCount++}> Key={prop.Key} : Value={prop.Value}");
        }
    }

    #endregion Receiving Messages

    #region Device Twin

    public static async Task CheckIfUpdateTwin(OpcClient opcClient)
    {
        var twin = await deviceClient.GetTwinAsync();

        if (created == false)
        {
            UpdateTwinAsync(string.Empty);
            created = true;
        }
        else
        {
            string errors = string.Empty;

            int deviceErrors = (int)opcClient.ReadNode($"ns=2;s=Device {Program.deviceID}/DeviceError").Value;

            if ((deviceErrors & Convert.ToInt32(Errors.Unknown)) != 0)
            {
                errors += "Unknown, ";
            }
            if ((deviceErrors & Convert.ToInt32(Errors.SensorFailue)) != 0)
            {
                errors += "SensorFailure, ";
            }
            if ((deviceErrors & Convert.ToInt32(Errors.PowerFailure)) != 0)
            {
                errors += "PowerFailure, ";
            }
            if ((deviceErrors & Convert.ToInt32(Errors.EmergencyStop)) != 0)
            {
                errors += "Emergency stop";
            }

            if (twin.Properties.Reported["deviceErrors"] != errors)
            {
                ifUpdate = true;
            }

            if (ifUpdate)
            {
                Console.WriteLine("   ----------------");
                Console.WriteLine($"   UDATING DEVICE {Program.deviceID}");
                Console.WriteLine("   ----------------");
                UpdateTwinAsync(errors);

            }
            else
            {
                Console.WriteLine("------------------");
                Console.WriteLine($"DEVICE {Program.deviceID} IS ACTUAL");
                Console.WriteLine("------------------");
            }
        }
        await Task.Delay(1000);
    }

    public static async Task UpdateTwinAsync(string deviceErrors)
    {
        var reportedProperties = new TwinCollection();
        ifUpdate = false;

        if (deviceErrors != string.Empty)
        {
            reportedProperties["deviceErrors"] = deviceErrors;
            reportedProperties["lastErrorDate"] = DateTime.Today;
        }
        else
        {
            reportedProperties["deviceErrors"] = string.Empty;
        }
        await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
    }

    public static async Task UpdateTwinValueAsync(string valueName, dynamic value)
    {
        var twin = await deviceClient.GetTwinAsync();

        var reportedProperties = new TwinCollection();
        reportedProperties[valueName] = value;

        await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
    }

    #endregion Device Twin

    #region Direct Methods
    #region EmergencyStop
    async Task EmergencyStop()
    {
        OPCDevice.EmergencyStop();
        await (Task.Delay(1000));
    }
    private async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
    {
        Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");
        await EmergencyStop();
        return new MethodResponse(0);
    }
    #endregion EmergencyStop
    #region ResetErrorStatus
    async Task ResetErrorStatus()
    {
        OPCDevice.ResetErrorStatus();
        await (Task.Delay(1000));
    }
    private async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
    {
        Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");
        await ResetErrorStatus();

        return new MethodResponse(0);
    }
    #endregion ResetErrorStatus
    #region SetProductionRate
    private static async Task<MethodResponse> SetProductionRateHandler(MethodRequest methodRequest, object userContext)
    {
        int value = int.Parse(methodRequest.DataAsJson);
        await OPCDevice.SetProductionRate(value);
        return new MethodResponse(0);
    }
    #endregion SetProductionRate

    private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
    {
        Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");
        await Task.Delay(1000);

        return new MethodResponse(0);
    }

    #endregion Direct Methods

    public async Task InitializeHandlers()
    {
        await deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, null);
        await deviceClient.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, null);
        await deviceClient.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, null);
        await deviceClient.SetMethodHandlerAsync("SetProductionRate", SetProductionRateHandler, deviceClient);
        await deviceClient.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, deviceClient);
    }
}
