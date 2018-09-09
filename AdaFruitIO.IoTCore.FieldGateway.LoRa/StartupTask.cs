//---------------------------------------------------------------------------------
// Copyright (c) September 2018, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Thanks to the creators and maintainers of the libraries used by this project
//    https://github.com/RSuter/NSwag
//---------------------------------------------------------------------------------
namespace devMobile.AdaFruitIO.IoTCore.FieldGateway.LoRa
{
	using System;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.IO;
	using System.Text;
	using System.Threading.Tasks;
	using AdaFruit.IO;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Converters;
	using Windows.ApplicationModel.Background;
	using Windows.Foundation.Diagnostics;
	using Windows.Storage;
	using devMobile.IoT.Rfm9x;

	public sealed class StartupTask : IBackgroundTask
	{
		private const string ConfigurationFilename = "config.json";

		// LoRa Hardware interface configuration
#if DRAGINO
		private const byte ChipSelectLine = 25;
		private const byte ResetLine = 17;
		private const byte InterruptLine = 4;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS0, ChipSelectLine, ResetLine, InterruptLine);
#endif
#if M2M
		private const byte ChipSelectLine = 25;
		private const byte ResetLine = 17;
		private const byte InterruptLine = 4;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS0, ChipSelectLine, ResetLine, InterruptLine);
#endif
#if ELECROW
		private const byte ResetLine = 22;
		private const byte InterruptLine = 25;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS1, ResetLine, InterruptLine);
#endif
#if ELECTRONIC_TRICKS
		private const byte ResetLine = 22;
		private const byte InterruptLine = 25;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS0, ResetLine, InterruptLine);
#endif
#if UPUTRONICS_RPIZERO_CS0
		private const byte InterruptLine = 25;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS0, InterruptLine);
#endif
#if UPUTRONICS_RPIZERO_CS1
		private const byte InterruptLine = 16;
		private Rfm9XDevice rfm9XDevice = new Rfm9XDevice(ChipSelectPin.CS1, InterruptLine);
#endif
		private const byte AddressLengthMinimum = 1;
		private const byte AddressLengthMaximum = 15;

		private const byte MessageLengthMinimum = 3;
		private const byte MessageLengthMaximum = 128;

		private readonly LoggingChannel loggingChannel = new LoggingChannel("devMobile AdaFruit.IO LoRa Field Gateway", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
		private readonly AdaFruit.IO.Client adaFruitIOClient = new AdaFruit.IO.Client();
		private ApplicationSettings applicationSettings = null;
		private BackgroundTaskDeferral deferral;

		public void Run(IBackgroundTaskInstance taskInstance)
		{
			if (!this.ConfigurationFileLoad().Result)
			{
				return;
			}

			// Configure the AdaFruit API client
			LoggingFields adaFruitIOSettings = new LoggingFields();
			if (!string.IsNullOrEmpty(this.applicationSettings.AdaFruitIOBaseUrl))
			{
				this.adaFruitIOClient.BaseUrl = this.applicationSettings.AdaFruitIOBaseUrl;
				adaFruitIOSettings.AddString("BaseURL", this.applicationSettings.AdaFruitIOBaseUrl);
			}

			this.adaFruitIOClient.ApiKey = this.applicationSettings.AdaFruitIOApiKey;

			adaFruitIOSettings.AddString("APIKey", this.applicationSettings.AdaFruitIOApiKey);
			adaFruitIOSettings.AddString("UserName", this.applicationSettings.AdaFruitIOUserName);
			adaFruitIOSettings.AddString("GroupName", this.applicationSettings.AdaFruitIOGroupName);
			this.loggingChannel.LogEvent("AdaFruit.IO configuration", adaFruitIOSettings, LoggingLevel.Information);

			rfm9XDevice.OnReceive += Rfm9XDevice_OnReceive;
			rfm9XDevice.OnTransmit += Rfm9XDevice_OnTransmit;

			rfm9XDevice.Initialise(this.applicationSettings.Frequency,
				rxDoneignoreIfCrcMissing: true,
				rxDoneignoreIfCrcInvalid: true,
				paBoost: this.applicationSettings.PABoost, maxPower: this.applicationSettings.MaxPower, outputPower: this.applicationSettings.OutputPower,
				ocpOn: this.applicationSettings.OCPOn, ocpTrim: this.applicationSettings.OCPTrim,
				lnaGain: this.applicationSettings.LnaGain, lnaBoost: this.applicationSettings.LNABoost,
				bandwidth: this.applicationSettings.Bandwidth, codingRate: this.applicationSettings.CodingRate, implicitHeaderModeOn: this.applicationSettings.ImplicitHeaderModeOn,
				spreadingFactor: this.applicationSettings.SpreadingFactor,
				rxPayloadCrcOn: true,
				symbolTimeout: this.applicationSettings.SymbolTimeout,
				preambleLength: this.applicationSettings.PreambleLength,
				payloadLength: this.applicationSettings.PayloadLength,
				payloadMaxLength: this.applicationSettings.PayloadMaxLength,
				freqHoppingPeriod: this.applicationSettings.FreqHoppingPeriod,
				lowDataRateOptimize: this.applicationSettings.LowDataRateOptimize,
				ppmCorrection: this.applicationSettings.PpmCorrection,

				detectionOptimize: this.applicationSettings.DetectionOptimize,
				invertIQ: this.applicationSettings.InvertIQ,
				detectionThreshold: this.applicationSettings.DetectionThreshold,
				syncWord: this.applicationSettings.SyncWord
				);

			#if DEBUG
				rfm9XDevice.RegisterDump();
			#endif

			rfm9XDevice.Receive(Encoding.UTF8.GetBytes(this.applicationSettings.Address));

			LoggingFields rf24Settings = new LoggingFields();
			rf24Settings.AddString("Address", this.applicationSettings.Address);
			rf24Settings.AddDouble("Frequency", this.applicationSettings.Frequency);
			rf24Settings.AddBoolean("DataRate", this.applicationSettings.PABoost);

			rf24Settings.AddUInt8("MaxPower", this.applicationSettings.MaxPower);
			rf24Settings.AddUInt8("OutputPower", this.applicationSettings.OutputPower);
			rf24Settings.AddBoolean("OCPOn", this.applicationSettings.OCPOn);
			rf24Settings.AddUInt8("OCPTrim", this.applicationSettings.OCPTrim);

			rf24Settings.AddString("LnaGain", this.applicationSettings.LnaGain.ToString());
			rf24Settings.AddBoolean("lnaBoost", this.applicationSettings.LNABoost);

			rf24Settings.AddString("codingRate", this.applicationSettings.CodingRate.ToString());
			rf24Settings.AddString("implicitHeaderModeOn", applicationSettings.ImplicitHeaderModeOn.ToString());
			rf24Settings.AddString("spreadingFactor", this.applicationSettings.SpreadingFactor.ToString());
			rf24Settings.AddBoolean("rxPayloadCrcOn", true);

			rf24Settings.AddUInt8("symbolTimeout", this.applicationSettings.SymbolTimeout);
			rf24Settings.AddUInt8("preambleLength", this.applicationSettings.PreambleLength);
			rf24Settings.AddUInt8("payloadLength", this.applicationSettings.PayloadLength);

			rf24Settings.AddUInt8("payloadMaxLength",this.applicationSettings.PayloadMaxLength);
			rf24Settings.AddUInt8("freqHoppingPeriod", this.applicationSettings.FreqHoppingPeriod);
			rf24Settings.AddBoolean("lowDataRateOptimize", this.applicationSettings.LowDataRateOptimize);
			rf24Settings.AddUInt8("ppmCorrection", this.applicationSettings.PpmCorrection);

			rf24Settings.AddString("detectionOptimize", this.applicationSettings.DetectionOptimize.ToString());
			rf24Settings.AddBoolean("invertIQ", this.applicationSettings.InvertIQ);
			rf24Settings.AddString("detectionThreshold", this.applicationSettings.DetectionThreshold.ToString());
			rf24Settings.AddUInt8("SyncWord", this.applicationSettings.SyncWord);

			this.loggingChannel.LogEvent("nRF24L01 configuration", rf24Settings, LoggingLevel.Information);

			this.deferral = taskInstance.GetDeferral();
		}

		private void Rfm9XDevice_OnTransmit(object sender, Rfm9XDevice.OnDataTransmitedEventArgs e)
		{
			throw new NotImplementedException();
		}

		private async Task<bool> ConfigurationFileLoad()
		{
			StorageFolder localFolder = ApplicationData.Current.LocalFolder;

			// Check to see if file exists
			if (localFolder.TryGetItemAsync(ConfigurationFilename).GetAwaiter().GetResult() == null)
			{
				this.loggingChannel.LogMessage("Configuration file " + ConfigurationFilename + " not found", LoggingLevel.Error);

				this.applicationSettings = new ApplicationSettings()
				{
					AdaFruitIOBaseUrl = "AdaFruit Base URL can go here",
					AdaFruitIOUserName = "AdaFruit User name goes here",
					AdaFruitIOApiKey = "AdaFruitIO API Key goes here",
					AdaFruitIOGroupName = "AdaFruit Group name goes here",
					Address = "Address here",
					Frequency = 915000000,
				};

				// Create empty configuration file
				StorageFile configurationFile = await localFolder.CreateFileAsync(ConfigurationFilename, CreationCollisionOption.OpenIfExists);
				using (Stream stream = await configurationFile.OpenStreamForWriteAsync())
				{
					using (TextWriter streamWriter = new StreamWriter(stream))
					{
						streamWriter.Write(JsonConvert.SerializeObject(this.applicationSettings, Formatting.Indented));
					}
				}

				return false;
			}

			try
			{
				// Load the configuration settings
				StorageFile configurationFile = (StorageFile)await localFolder.TryGetItemAsync(ConfigurationFilename);

				using (Stream stream = await configurationFile.OpenStreamForReadAsync())
				{
					using (StreamReader streamReader = new StreamReader(stream))
					{
						this.applicationSettings = JsonConvert.DeserializeObject<ApplicationSettings>(streamReader.ReadToEnd());
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				this.loggingChannel.LogMessage("Configuration file " + ConfigurationFilename + " load failed " + ex.Message, LoggingLevel.Error);
				return false;
			}
		}

		private async void Rfm9XDevice_OnReceive(object sender, Rfm9XDevice.OnDataReceivedEventArgs e)
		{
			char[] sensorReadingSeparator = new char[] { ',' };
			char[] sensorIdAndValueSeparator = new char[] { ' ' };

			string addressText = UTF8Encoding.UTF8.GetString(e.Address);
			string addressBcdText = BitConverter.ToString(e.Address);
			string messageText = UTF8Encoding.UTF8.GetString(e.Data);
			string messageBcdText = BitConverter.ToString(e.Data);

#if DEBUG
			Debug.WriteLine(@"{0:HH:mm:ss}-RX From {1} PacketSnr {2:0.0} Packet RSSI {3}dBm RSSI {4}dBm = {5} byte message ""{6}""", DateTime.Now, addressText, e.PacketSnr, e.PacketRssi, e.Rssi, e.Data.Length, messageText);
#endif
			LoggingFields messagePayload = new LoggingFields();
			messagePayload.AddInt32("AddressLength", e.Address.Length);
			messagePayload.AddString("Address-BCD", addressBcdText);
			messagePayload.AddString("Address-Unicode", addressText);
			messagePayload.AddInt32("Message-Length", e.Data.Length);
			messagePayload.AddString("Message-BCD", messageBcdText);
			messagePayload.AddString("Nessage-Unicode", messageText);
			messagePayload.AddDouble("Packet SNR", e.PacketSnr);
			messagePayload.AddInt32("Packet RSSI", e.PacketRssi);
			messagePayload.AddInt32("RSSI", e.Rssi);
			this.loggingChannel.LogEvent("Message Data", messagePayload, LoggingLevel.Verbose);

			
			// Check the address is not to short/long 
			if (e.Address.Length < AddressLengthMinimum)
			{
				this.loggingChannel.LogMessage("From address too short", LoggingLevel.Warning);
				return;
			}

			if (e.Address.Length > MessageLengthMaximum)
			{
				this.loggingChannel.LogMessage("From address too long", LoggingLevel.Warning);
				return;
			}

			// Check the payload is not too short/long 
			if (e.Data.Length < MessageLengthMinimum)
			{
				this.loggingChannel.LogMessage("Message too short to contain any data", LoggingLevel.Warning);
				return;
			}

			if (e.Data.Length > MessageLengthMaximum)
			{
				this.loggingChannel.LogMessage("Message too long to contain valid data", LoggingLevel.Warning);
				return;
			}

			// Adafruit IO is case sensitive & onlye does lower case ?
			string deviceId = addressText.ToLower();

			// Chop up the CSV text payload
			string[] sensorReadings = messageText.Split(sensorReadingSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (sensorReadings.Length == 0)
			{
				this.loggingChannel.LogMessage("Payload contains no sensor readings", LoggingLevel.Warning);
				return;
			}

			Group_feed_data groupFeedData = new Group_feed_data();

			LoggingFields sensorData = new LoggingFields();
			sensorData.AddString("DeviceID", deviceId);

			// Chop up each sensor reading into an ID & value
			foreach (string sensorReading in sensorReadings)
			{
				string[] sensorIdAndValue = sensorReading.Split(sensorIdAndValueSeparator, StringSplitOptions.RemoveEmptyEntries);

				// Check that there is an id & value
				if (sensorIdAndValue.Length != 2)
				{
					this.loggingChannel.LogMessage("Sensor reading invalid format", LoggingLevel.Warning);
					return;
				}

				string sensorId = sensorIdAndValue[0].ToLower();
				string value = sensorIdAndValue[1];

				// Construct the sensor ID from SensordeviceID & Value ID
				groupFeedData.Feeds.Add(new Anonymous2() { Key = string.Format("{0}{1}", deviceId, sensorId), Value = value });

				sensorData.AddString(sensorId, value);

				Debug.WriteLine(" Sensor {0}{1} Value {2}", deviceId, sensorId, value);
			}

			this.loggingChannel.LogEvent("Sensor readings", sensorData, LoggingLevel.Verbose);

			try
			{
				Debug.WriteLine(" CreateGroupDataAsync start");
				await this.adaFruitIOClient.CreateGroupDataAsync(this.applicationSettings.AdaFruitIOUserName, this.applicationSettings.AdaFruitIOGroupName.ToLower(), groupFeedData);
				Debug.WriteLine(" CreateGroupDataAsync finish");
			}
			catch (Exception ex)
			{
				Debug.WriteLine(" CreateGroupDataAsync failed {0}", ex.Message);
				this.loggingChannel.LogMessage("CreateGroupDataAsync failed " + ex.Message, LoggingLevel.Error);
			}
		}

		private class ApplicationSettings
		{
			[JsonProperty("AdaFruitIOBaseUrl", Required = Required.DisallowNull)]
			public string AdaFruitIOBaseUrl { get; set; }

			[JsonProperty("AdaFruitIOUserName", Required = Required.Always)]
			public string AdaFruitIOUserName { get; set; }

			[JsonProperty("AdaFruitIOApiKey", Required = Required.Always)]
			public string AdaFruitIOApiKey { get; set; }

			[JsonProperty("AdaFruitIOGroupName", Required = Required.Always)]
			public string AdaFruitIOGroupName { get; set; }


			// LoRa configuration parameters
			[JsonProperty("Address", Required = Required.Always)]
			public string Address { get; set; }

			[DefaultValue(Rfm9XDevice.FrequencyDefault)]
			[JsonProperty("Frequency", DefaultValueHandling = DefaultValueHandling.Populate)]
			public double Frequency { get; set; }

			// RegPaConfig
			[DefaultValue(Rfm9XDevice.PABoostDefault)]
			[JsonProperty("PABoost", DefaultValueHandling = DefaultValueHandling.Populate)]
			public bool PABoost { get; set; }

			[DefaultValue(Rfm9XDevice.RegPAConfigMaxPowerDefault)]
			[JsonProperty("MaxPower", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte MaxPower { get; set; }

			[DefaultValue(Rfm9XDevice.RegPAConfigOutputPowerDefault)]
			[JsonProperty("OutputPower", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte OutputPower { get; set; }

			// RegOcp
			[DefaultValue(Rfm9XDevice.RegOcpDefault)]
			[JsonProperty("OCPOn", DefaultValueHandling = DefaultValueHandling.Populate)]
			public bool OCPOn { get; set; }

			[DefaultValue(Rfm9XDevice.RegOcpOcpTrimDefault)]
			[JsonProperty("OCPTrim", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte OCPTrim { get; set; }

			// RegLna
			[DefaultValue(Rfm9XDevice.LnaGainDefault)]
			[JsonProperty("LNAGain", DefaultValueHandling = DefaultValueHandling.Populate)]
			[JsonConverter(typeof(StringEnumConverter))]
			public Rfm9XDevice.RegLnaLnaGain LnaGain { get; set; }

			[DefaultValue(Rfm9XDevice.LnaBoostDefault)]
			[JsonProperty("LNABoost", DefaultValueHandling = DefaultValueHandling.Populate)]
			public bool LNABoost { get; set; }

			// RegModemConfig1
			[DefaultValue(Rfm9XDevice.RegModemConfigBandwidthDefault)]
			[JsonProperty("Bandwidth", DefaultValueHandling = DefaultValueHandling.Populate)]
			[JsonConverter(typeof(StringEnumConverter))]
			public Rfm9XDevice.RegModemConfigBandwidth Bandwidth { get; set; }

			[DefaultValue(Rfm9XDevice.RegModemConfigCodingRateDefault)]
			[JsonProperty("codingRate", DefaultValueHandling = DefaultValueHandling.Populate)]
			[JsonConverter(typeof(StringEnumConverter))]
			public Rfm9XDevice.RegModemConfigCodingRate CodingRate { get; set; }

			[DefaultValue(Rfm9XDevice.RegModemConfigImplicitHeaderModeOnDefault)]
			[JsonProperty("ImplicitHeaderModeOn", DefaultValueHandling = DefaultValueHandling.Populate)]
			[JsonConverter(typeof(StringEnumConverter))]
			public Rfm9XDevice.RegModemConfigImplicitHeaderModeOn ImplicitHeaderModeOn { get; set; }

			// RegModemConfig2SpreadingFactor
			[DefaultValue(Rfm9XDevice.RegModemConfig2SpreadingFactorDefault)]
			[JsonProperty("SpreadingFactor", DefaultValueHandling = DefaultValueHandling.Populate)]
			public Rfm9XDevice.RegModemConfig2SpreadingFactor SpreadingFactor { get; set; }

			[DefaultValue(Rfm9XDevice.SymbolTimeoutDefault)]
			[JsonProperty("SymbolTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte SymbolTimeout { get; set; }

			[DefaultValue(Rfm9XDevice.PreambleLengthDefault)]
			[JsonProperty("PreambleLength", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte PreambleLength { get; set; }

			[DefaultValue(Rfm9XDevice.PayloadLengthDefault)]
			[JsonProperty("PayloadLength", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte PayloadLength { get; set; }

			[DefaultValue(Rfm9XDevice.PayloadMaxLengthDefault)]
			[JsonProperty("PayloadMaxLength", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte PayloadMaxLength { get; set; }

			[DefaultValue(Rfm9XDevice.FreqHoppingPeriodDefault)]
			[JsonProperty("freqHoppingPeriod", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte FreqHoppingPeriod { get; set; }

			[DefaultValue(Rfm9XDevice.LowDataRateOptimizeDefault)]
			[JsonProperty("LowDataRateOptimize", DefaultValueHandling = DefaultValueHandling.Populate)]
			public bool LowDataRateOptimize { get; set; }

			[DefaultValue(Rfm9XDevice.AgcAutoOnDefault)]
			[JsonProperty("AgcAutoOn", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte AgcAutoOn { get; set; }

			[DefaultValue(Rfm9XDevice.ppmCorrectionDefault)]
			[JsonProperty("PPMCorrection", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte PpmCorrection { get; set; }

			[DefaultValue(Rfm9XDevice.RegDetectOptimizeDectionOptimizeDefault)]
			[JsonProperty("DetectionOptimize", DefaultValueHandling = DefaultValueHandling.Populate)]
			public Rfm9XDevice.RegDetectOptimizeDectionOptimize DetectionOptimize { get; set; }

			[DefaultValue(Rfm9XDevice.InvertIqDefault)]
			[JsonProperty("InvertIQ", DefaultValueHandling = DefaultValueHandling.Populate)]
			public bool InvertIQ { get; set; }

			[DefaultValue(Rfm9XDevice.RegisterDetectionThresholdDefault)]
			[JsonProperty("DetectionThreshold", DefaultValueHandling = DefaultValueHandling.Populate)]
			public Rfm9XDevice.RegisterDetectionThreshold DetectionThreshold { get; set; }

			[DefaultValue(Rfm9XDevice.RegSyncWordDefault)]
			[JsonProperty("SyncWord", DefaultValueHandling = DefaultValueHandling.Populate)]
			public byte SyncWord { get; set; }
		}
	}
}
