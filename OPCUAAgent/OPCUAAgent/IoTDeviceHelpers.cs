using Microsoft.Azure.Devices.Shared;
using Opc.UaFx.Client;
using System;
using System.Threading.Tasks;

internal static class IoTDeviceHelpers
{
    public static async Task CheckIfUpdateTwin(OpcClient opcClient)
    {
        var twin = await IoTDevice.deviceClient.GetTwinAsync();

        if (IoTDevice.created == false)
        {
            // Inicjalna aktualizacja Device Twin
            UpdateTwinAsync(string.Empty, 0);
            IoTDevice.created = true;
        }
        else
        {
            string errors = string.Empty;
            int deviceErrors = (int)opcClient.ReadNode($"ns=2;s=Device {Program.deviceID}/DeviceError").Value;
            int productionRate = (int)opcClient.ReadNode($"ns=2;s=Device {Program.deviceID}/ProductionRate").Value;

            // Sprawdzanie błędów urządzenia
            if ((deviceErrors & Convert.ToInt32(Errors.Unknown)) != 0) errors += "Unknown, ";
            if ((deviceErrors & Convert.ToInt32(Errors.SensorFailue)) != 0) errors += "SensorFailure, ";
            if ((deviceErrors & Convert.ToInt32(Errors.PowerFailure)) != 0) errors += "PowerFailure, ";
            if ((deviceErrors & Convert.ToInt32(Errors.EmergencyStop)) != 0) errors += "Emergency stop";

            // Sprawdzanie czy dane w Twin są aktualne
            if (twin.Properties.Reported["deviceErrors"] != errors)
            {
                IoTDevice.ifUpdate = true;
            }

            if (twin.Properties.Reported["productionRate"] != productionRate)
            {
                IoTDevice.ifUpdate = true;
            }

            if (IoTDevice.ifUpdate)
            {
                Console.WriteLine("   ----------------");
                Console.WriteLine($"   UPDATING DEVICE {Program.deviceID}");
                Console.WriteLine("   ----------------");
                UpdateTwinAsync(errors, productionRate);
            }
            else
            {
                Console.WriteLine("------------------");
                Console.WriteLine($"DEVICE {Program.deviceID} IS UP TO DATE");
                Console.WriteLine("------------------");
            }
        }
        await Task.Delay(1000);
    }

    public static async Task UpdateTwinAsync(string deviceErrors, int productionRate)
    {
        var reportedProperties = new TwinCollection();
        IoTDevice.ifUpdate = false;

        reportedProperties["productionRate"] = productionRate; // Ustaw Production Rate w Device Twin
        if (!string.IsNullOrEmpty(deviceErrors))
        {
            reportedProperties["deviceErrors"] = deviceErrors;
            reportedProperties["lastErrorDate"] = DateTime.Today;
        }
        else
        {
            reportedProperties["deviceErrors"] = string.Empty;
        }

        await IoTDevice.deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
    }
}
