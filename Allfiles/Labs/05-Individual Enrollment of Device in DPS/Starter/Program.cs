// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SymmetricKeySimulatedDevice
{
    class Program
    {
        // ////////////////////////////////////////////////////////

        // Azure Device Provisioning Service (DPS) ID Scope
        private static string dpsIdScope = "0ne000F332A";
        // Registration ID
        private static string registrationId = "DPSSimulatedDevice1";
        // Individual Enrollment Primary Key
        private const string individualEnrollmentPrimaryKey = "NFM+39GhQokV3FoBzfSmu4g+8QuB5GiJSIUI+7zm2cmIqMFwIM+c2pQszOPkWx04U2HpS6sb4+LG60+p39sP9g==";
        // Individual Enrollment Secondary Key
        private const string individualEnrollmentSecondaryKey = "lgt9teKLMrYD0LRlkdp/yWy8FsFogduFLQI5h1FpPtF2AhPfNmyn+aVzkoc56gH/zD+ZBGEhwaQQMdEbGciZAQ==";

        // ////////////////////////////////////////////////////////

        private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";

        public static int Main(string[] args)
        {
            if (string.IsNullOrWhiteSpace(dpsIdScope) && (args.Length > 0))
            {
                dpsIdScope = args[0];
            }

            if (string.IsNullOrWhiteSpace(dpsIdScope))
            {
                Console.WriteLine("ProvisioningDeviceClientSymmetricKey <IDScope>");
                return 1;
            }

            string primaryKey = string.Empty;
            string secondaryKey = string.Empty;
            if (!String.IsNullOrEmpty(registrationId) && !String.IsNullOrEmpty(individualEnrollmentPrimaryKey) && !String.IsNullOrEmpty(individualEnrollmentSecondaryKey))
            {
                //Individual enrollment flow, the primary and secondary keys are the same as the individual enrollment keys
                primaryKey = individualEnrollmentPrimaryKey;
                secondaryKey = individualEnrollmentSecondaryKey;
            }
            else
            {
                Console.WriteLine("Invalid configuration provided, must provide individual enrollment keys");
                return -1;
            }

            using (var security = new SecurityProviderSymmetricKey(registrationId, primaryKey, secondaryKey))



            using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
            {
                ProvisioningDeviceClient provClient =
                    ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, dpsIdScope, security, transport);


                var provisioningDeviceLogic = new ProvisioningDeviceLogic(provClient, security);
                provisioningDeviceLogic.RunAsync().GetAwaiter().GetResult();
            }


            return 0;
        }
    }

    public class ProvisioningDeviceLogic
    {
        #region Constructor

        readonly ProvisioningDeviceClient _provClient;
        readonly SecurityProvider _security;
        DeviceClient iotClient;

        // Delay between Telemetry readings in Seconds (default to 1 second)
        private int _telemetryDelay = 1;

        public ProvisioningDeviceLogic(ProvisioningDeviceClient provisioningDeviceClient, SecurityProvider security)
        {
            _provClient = provisioningDeviceClient;
            _security = security;
        }

        #endregion

        public async Task RunAsync()
        {
            Console.WriteLine($"RegistrationID = {_security.GetRegistrationID()}");

            Console.Write("ProvisioningClient RegisterAsync . . . ");
            DeviceRegistrationResult result = await _provClient.RegisterAsync().ConfigureAwait(false);

            Console.WriteLine($"Device Registration Status: {result.Status}");
            Console.WriteLine($"ProvisioningClient AssignedHub: {result.AssignedHub}; DeviceID: {result.DeviceId}");

            if (result.Status != ProvisioningRegistrationStatusType.Assigned)
            {
                throw new Exception($"DeviceRegistrationResult.Status is NOT 'Assigned'");
            }

            Console.WriteLine("Creating Symmetric Key DeviceClient authentication");
            IAuthenticationMethod auth = new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, (_security as SecurityProviderSymmetricKey).GetPrimaryKey());


            Console.WriteLine("Simulated Device. Ctrl-C to exit.");
            using (iotClient = DeviceClient.Create(result.AssignedHub, auth, TransportType.Amqp))
            {
                Console.WriteLine("DeviceClient OpenAsync.");
                await iotClient.OpenAsync().ConfigureAwait(false);


                 Console.WriteLine("Connecting SetDesiredPropertyUpdateCallbackAsync event handler...");
                 await iotClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null).ConfigureAwait(false);
                

                Console.WriteLine("Loading device twin Properties...");
                var twin = await iotClient.GetTwinAsync().ConfigureAwait(false);
                await OnDesiredPropertyChanged(twin.Properties.Desired, null);


                // Start reading and sending device telemetry
                Console.WriteLine("Start reading and sending device telemetry...");
                await SendDeviceToCloudMessagesAsync(iotClient);

                //Console.WriteLine("DeviceClient CloseAsync.");
                //await iotClient.CloseAsync().ConfigureAwait(false);
            }
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired Twin Property Changed:");
            Console.WriteLine($"{desiredProperties.ToJson()}");

            // Read the desired Twin Properties
            if (desiredProperties.Contains("telemetryDelay"))
            {
                string desiredTelemetryDelay = desiredProperties["telemetryDelay"];
                if (desiredTelemetryDelay != null)
                {
                    this._telemetryDelay = int.Parse(desiredTelemetryDelay);
                }
                // if desired telemetryDelay is null or unspecified, don't change it
            }


            // Report Twin Properties
            var reportedProperties = new TwinCollection();
            reportedProperties["telemetryDelay"] = this._telemetryDelay.ToString();
            await iotClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
            Console.WriteLine("Reported Twin Properties:");
            Console.WriteLine($"{reportedProperties.ToJson()}");
        }
        private async Task SendDeviceToCloudMessagesAsync(DeviceClient deviceClient)
        {
            // Initial telemetry values
            double minTemperature = 20;
            double minHumidity = 60;
            double minPressure = 1013.25;
            double minLatitude = 39.810492;
            double minLongitude = -98.556061;
            Random rand = new Random();

            while (true)
            {
                double currentTemperature = minTemperature + rand.NextDouble() * 15;
                double currentHumidity = minHumidity + rand.NextDouble() * 20;
                double currentPressure = minPressure + rand.NextDouble() * 12;
                double currentLatitude = minLatitude + rand.NextDouble() * 0.5;
                double currentLongitude = minLongitude + rand.NextDouble() * 0.5;

                // Create JSON message
                var telemetryDataPoint = new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                    pressure = currentPressure,
                    latitude = currentLatitude,
                    longitude = currentLongitude
                };
                var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                var message = new Message(Encoding.ASCII.GetBytes(messageString));

                // Add a custom application property to the message.
                // An IoT hub can filter on these properties without access to the message body.
                message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

                // Send the telemetry message
                await deviceClient.SendEventAsync(message);
                Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);

                // Delay before next Telemetry reading
                await Task.Delay(this._telemetryDelay * 1000);
            }
        }

    }
}
