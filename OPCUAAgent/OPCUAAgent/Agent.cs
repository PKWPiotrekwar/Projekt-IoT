using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;

public class Program
{
    public static DateTime maintenanceDate = DateTime.MinValue;
    public static int deviceID;

    static async Task Main(string[] args)
    {
        OPCDevice opcDevice = new OPCDevice();

        OPCDevice.Start();

        Console.WriteLine("Input Device ID(number)");
        deviceID = Convert.ToInt32(Console.ReadLine());

        Console.WriteLine(File.ReadAllLines($"../../../../../Settings.txt")[2 + deviceID]);
        using var deviceClient = DeviceClient.CreateFromConnectionString(File.ReadAllLines($"../../../../../Settings.txt")[2 + deviceID], TransportType.Mqtt);
        await deviceClient.OpenAsync();

        var device = new IoTDevice(deviceClient);

        await device.InitializeHandlers();

        //odczytywanie i wysylanie co sekunde
        var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await periodicTimer.WaitForNextTickAsync())
        {
            IoTDevice.OneDeviceMagic(OPCDevice.client, deviceID);
        }

        OPCDevice.End();
        Console.ReadKey(true);
    }
}