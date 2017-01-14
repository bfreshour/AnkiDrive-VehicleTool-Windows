using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using static AnkiDriveVehicleTool.AnkiBLE;

namespace AnkiDriveVehicleTool
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync().Wait();
        }

        static async Task MainAsync()
        {
            BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher();
            bool result = false;
            GattDeviceService service = null;
            GattCharacteristic readChar = null;
            GattCharacteristic writeChar = null;
            anki_vehicle myVehicle;

            var ble = new AnkiBLE { };

            watcher.Received += ble.OnAdvertisementReceived;
            Console.Write("Searching for cars, press any key to stop... ");
            watcher.Start();
            Console.ReadLine();
            watcher.Stop();
            Console.WriteLine();
            if (ble.lstAnki.Count() == 0)
            {
                Console.WriteLine("No cars found.  Exiting...");
                return;
            }
            while (true)
            {
                Console.WriteLine("Cars available for connection: ");
                int i = 0;
                foreach(var car in ble.lstAnki)
                {
                    var mac = String.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}", (car.mac_address >> (8 * 5)) & 0xff, (car.mac_address >> (8 * 4)) & 0xff, (car.mac_address >> (8 * 3)) & 0xff, (car.mac_address >> (8 * 2)) & 0xff, (car.mac_address >> (8 * 1)) & 0xff, (car.mac_address >> (8 * 0)) & 0xff);
                    Console.WriteLine("{0}: {1} ({2})", new object[] { i, (AnkiModel)car.model_id, mac });
                    i++;
                }
                Console.WriteLine();
                Console.WriteLine("Please choose a car or type 'exit': ");
                string input = Console.ReadLine();
                if (input == "exit")
                    return;
                int selection;
                if(Int32.TryParse(input, out selection))
                {
                    if(selection <= ble.lstAnki.Count())
                    {
                        myVehicle = ble.lstAnki[selection];
                        Console.WriteLine();
                        break;
                    }
                }
            }

            result = await IsPairedUnpair(myVehicle);
            result = await Pair(myVehicle);

            if (result)
            {
                Console.Write("Connecting (press ctrl-c to cancel)...");
                bool tryAgain = true;
                while (tryAgain)
                {
                    try
                    {
                        service = await waitConnection(myVehicle);

                        readChar = service.GetCharacteristics(Guid.Parse("be15bee0-6186-407e-8381-0bd89c4d8df4")).FirstOrDefault();
                        writeChar = service.GetCharacteristics(Guid.Parse("be15bee1-6186-407e-8381-0bd89c4d8df4")).FirstOrDefault();
                        tryAgain = false;
                    }
                    catch (Exception ex)
                    {
                        Console.Write(".");
                        await Task.Delay(500);
                        readChar = null;
                        writeChar = null;
                        service = null; 
                    }
                }
                if(readChar != null && writeChar != null)
                {
                    Console.WriteLine();
                    Interact(myVehicle, readChar, writeChar);
                }
            }
            await Unpair(myVehicle);
            Console.WriteLine("Exiting...");
        }

        private async static Task<GattDeviceService> waitConnection(AnkiBLE.anki_vehicle vehicle)
        {
            BluetoothLEDevice bluetoothLeDevice = null;
            GattDeviceService service = null;
            bluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(vehicle.mac_address);
            while (bluetoothLeDevice.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                throw new Exception("Not connected!");
            }
            service = bluetoothLeDevice.GetGattService(Guid.Parse("be15beef-6186-407e-8381-0bd89c4d8df4"));
            return service;
        }

        public static async void Interact(anki_vehicle vehicle, GattCharacteristic readChar, GattCharacteristic writeChar)
        {
            try
            {
                var writer = new Windows.Storage.Streams.DataWriter();
                byte[] rawBytes = null;
                GattCommunicationStatus result;
                readChar.ValueChanged += VehicleResponse;
                if (readChar.CharacteristicProperties == (GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify))
                    result = await readChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                while (true)
                {
                    Console.Write("Please enter your command: ");
                    string input = Console.ReadLine();
                    if (input == "exit")
                        return;

                    switch(input)
                    {
                        case "ping":
                            {
                                anki_vehicle_msg msg = new anki_vehicle_msg();
                                msg.size = ANKI_VEHICLE_MSG_BASE_SIZE;
                                msg.msg_id = (byte)AnkiMessage.ANKI_VEHICLE_MSG_C2V_PING_REQUEST;
                                rawBytes = getBytes(msg);
                                writer.WriteBytes(rawBytes);
                                result = await writeChar.WriteValueAsync(writer.DetachBuffer());
                                break;
                            }
                        case "get-version":
                            {
                                anki_vehicle_msg msg = new anki_vehicle_msg();
                                msg.size = ANKI_VEHICLE_MSG_BASE_SIZE;
                                msg.msg_id = (byte)AnkiMessage.ANKI_VEHICLE_MSG_C2V_VERSION_REQUEST;
                                rawBytes = getBytes(msg);
                                writer.WriteBytes(rawBytes);
                                result = await writeChar.WriteValueAsync(writer.DetachBuffer());
                                break;
                            }
                        case "get-battery":
                            {
                                anki_vehicle_msg msg = new anki_vehicle_msg();
                                msg.size = ANKI_VEHICLE_MSG_BASE_SIZE;
                                msg.msg_id = (byte)AnkiMessage.ANKI_VEHICLE_MSG_C2V_BATTERY_LEVEL_REQUEST;
                                rawBytes = getBytes(msg);
                                writer.WriteBytes(rawBytes);
                                result = await writeChar.WriteValueAsync(writer.DetachBuffer());
                                break;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static void VehicleResponse(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var rawBytes = readGattResult(args.CharacteristicValue);

            var anki_vehicle_msg = new anki_vehicle_msg();

            anki_vehicle_msg.size = rawBytes[0];
            anki_vehicle_msg.msg_id = rawBytes[1];

            if (rawBytes.Length > 2)
            {
                anki_vehicle_msg.payload = new byte[rawBytes.Length - 2];
                Array.Copy(rawBytes, 2, anki_vehicle_msg.payload, 0, rawBytes.Length - 2);
            }

            switch (anki_vehicle_msg.msg_id)
            {
                case (byte)AnkiMessage.ANKI_VEHICLE_MSG_V2C_PING_RESPONSE:
                    {
                        Console.WriteLine("[read] PING_RESPONSE\n");
                        break;
                    }
                case (byte)AnkiMessage.ANKI_VEHICLE_MSG_V2C_VERSION_RESPONSE:
                    {
                        Console.WriteLine("[read] VERSION_RESPONSE: {0}", BitConverter.ToUInt16(anki_vehicle_msg.payload, 0));
                        break;
                    }
                case (byte)AnkiMessage.ANKI_VEHICLE_MSG_V2C_BATTERY_LEVEL_RESPONSE:
                    {
                        Console.WriteLine("[read] BATTERY_RESPONSE: {0}", BitConverter.ToUInt16(anki_vehicle_msg.payload, 0));
                        break;
                    }
                default:
                    {
                        Console.WriteLine("[read] RESPONSE: {0}", GetHexStringFrom(anki_vehicle_msg.payload));
                        break;
                    }
            }
        }

        static byte[] readGattResult(IBuffer value)
        {
            byte[] data = new byte[value.Length];
            using (var reader = DataReader.FromBuffer(value))
            {
                reader.ReadBytes(data);
            }
            return data;
        }

        static byte[] getBytes(object str)
        {
            int size = Marshal.SizeOf(str);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        static async Task<bool> IsPairedUnpair(AnkiBLE.anki_vehicle vehicle)
        {
            var selector = BluetoothDevice.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync("System.Devices.DevObjectType:= 5 AND System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\" AND (System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True OR System.Devices.Aep.Bluetooth.IssueInquiry:=System.StructuredQueryType.Boolean#False)");
            if(devices.Count() > 0)
            {
                BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(vehicle.mac_address);
                foreach(var dev in devices)
                {
                    if (dev.Id == device.DeviceInformation.Id)
                    {
                        await Unpair(vehicle);
                        return true;
                    }
                        
                }
            }
            return false;
        }

        static async Task<bool> Pair(AnkiBLE.anki_vehicle vehicle)
        {
            BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(vehicle.mac_address);
            DeviceInformationCustomPairing customPairing = device.DeviceInformation.Pairing.Custom;
            customPairing.PairingRequested += PairingRequestedHandler;
            DevicePairingResult result = await customPairing.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.None);
            customPairing.PairingRequested -= PairingRequestedHandler;
            return (result.Status == DevicePairingResultStatus.Paired || result.Status == DevicePairingResultStatus.AlreadyPaired);
        }

        static async Task<bool> Unpair(AnkiBLE.anki_vehicle vehicle)
        {
            BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(vehicle.mac_address);
            DeviceUnpairingResult result = await device.DeviceInformation.Pairing.UnpairAsync();
            return (result.Status == DeviceUnpairingResultStatus.Unpaired);
        }

        private static async void PairingRequestedHandler(
            DeviceInformationCustomPairing sender,
            DevicePairingRequestedEventArgs args)
        {
            args.Accept();
        }

    }

    class AnkiBLE
    {
        int VEHICLE_STATE_FULL_BATTERY = (1 << 4);
        int VEHICLE_STATE_LOW_BATTERY = (1 << 5);
        int VEHICLE_STATE_ON_CHARGER = (1 << 6);
        public List<anki_vehicle> lstAnki = new List<anki_vehicle>();
        public bool rawBytes = false;
        public bool displayOutput = false;

        public static byte ANKI_VEHICLE_MSG_MAX_SIZE = 20;
        public static byte ANKI_VEHICLE_MSG_PAYLOAD_MAX_SIZE = 18;
        public static byte ANKI_VEHICLE_MSG_BASE_SIZE = 1;

        public async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            bool foundNewCar = false;
            byte[] localName = null;
            DeviceInformation DeviceInfo = null;
            bool connectFailure = false;
            string outputBuffer = "";

            string strMacAddr = String.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                (eventArgs.BluetoothAddress >> (8 * 5)) & 0xff,
                (eventArgs.BluetoothAddress >> (8 * 4)) & 0xff,
                (eventArgs.BluetoothAddress >> (8 * 3)) & 0xff,
                (eventArgs.BluetoothAddress >> (8 * 2)) & 0xff,
                (eventArgs.BluetoothAddress >> (8 * 1)) & 0xff,
                (eventArgs.BluetoothAddress >> (8 * 0)) & 0xff);
            outputBuffer = "Advertisement:\r\n";
            outputBuffer += "MAC: " + strMacAddr + " - Signal Strength: " + eventArgs.RawSignalStrengthInDBm + "dBm\r\n";

            /*
            foreach (var temp in eventArgs.Advertisement.ServiceUuids)
            {
                displayOutput += "Service UUID: " + temp.ToString());
                if(temp.ToString() == "be15beef-6186-407e-8381-0bd89c4d8df4")
                {
                    try
                    {
                        BluetoothLEDevice bleDevice = Connect(eventArgs.BluetoothAddress).Result;
                        localName = Encoding.ASCII.GetBytes(bleDevice.Name);
                        DeviceInfo = bleDevice.DeviceInformation;
                        bleDevice?.Dispose();
                        bleDevice = null;
                    }
                    catch(Exception ex)
                    {
                        connectFailure = true;
                    }
                } else
                {
                    return;
                }
            }
            */
            // Does this exist in our list...
            var car = lstAnki.Where(c => c.mac_address == eventArgs.BluetoothAddress).FirstOrDefault();
            if (car == null)
            {
                car = new anki_vehicle();
                car.mac_address = eventArgs.BluetoothAddress;
                foundNewCar = true;
            }

            if(eventArgs.Advertisement.LocalName != "")
            {
                localName = Encoding.ASCII.GetBytes(eventArgs.Advertisement.LocalName);
                if (rawBytes)
                {
                    outputBuffer += "LocalName: ";
                    outputBuffer += GetHexStringFrom(localName) + "\r\n";
                }
                var state = localName[0] & 0xff;
                byte[] name = new byte[localName.Length-8-1];
                Array.Copy(localName, 8, name, 0, localName.Length - 8 - 1); // Minus 1 additional to avoid null terminator...
                outputBuffer += "Name: " + Encoding.ASCII.GetString(name) + "\r\n";
                outputBuffer += "Battery Full: " + IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_FULL_BATTERY) + "\r\n";
                outputBuffer += "Battery Low: " + IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_LOW_BATTERY) + "\r\n";
                outputBuffer += "On Charger: " + IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_ON_CHARGER) + "\r\n";
                car.name = Encoding.ASCII.GetString(name);
                car.full_battery = IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_FULL_BATTERY);
                car.low_battery = IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_LOW_BATTERY);
                car.on_charger = IS_VEHICLE_STATE_SET(state, VEHICLE_STATE_ON_CHARGER);
            }

            var i = 0;
            foreach (var temp in eventArgs.Advertisement.ManufacturerData)
            {
                var data = new byte[temp.Data.Length];
                using (var reader = DataReader.FromBuffer(temp.Data))
                {
                    reader.ReadBytes(data);
                }
                if (rawBytes)
                {
                    outputBuffer += "Manuf Data " + i.ToString() + ": ";
                    outputBuffer += GetHexStringFrom(data) + "\r\n";
                }
                i++;
            }
            i = 0;
            foreach (var temp in eventArgs.Advertisement.DataSections)
            {
                var data = new byte[temp.Data.Length];
                using (var reader = DataReader.FromBuffer(temp.Data))
                {
                    reader.ReadBytes(data);
                }
                if (rawBytes)
                {
                    outputBuffer += "Data Section " + i.ToString() + ": ";
                    outputBuffer += GetHexStringFrom(data) + "\r\n";
                }
                var str = Encoding.ASCII.GetString(data);
                if (i == 2)
                {
                    outputBuffer += "ID: " + (data[7] | (data[6] << 8) | (data[5] << 16) | (data[4] << 24)) + "\r\n";
                    outputBuffer += "Model: " + Enum.GetName(typeof(AnkiModel), data[3]) + "\r\n";
                    outputBuffer += "Product ID: " + (data[1] | (data[0] << 8)) + "\r\n";
                    car.identifier = (uint)((data[7] | (data[6] << 8) | (data[5] << 16) | (data[4] << 24)));
                    car.model_id = (AnkiModel)data[3];
                    car.product_id = (uint)(data[1] | (data[0] << 8));
                }
                i++;
            }

            /*
            if (connectFailure)
                Console.WriteLine("Failure to connect to vehicle.  Try restarting bluetooth on your PC or reseting the vehicle by holding down the power button until it flashes.");
            */
            if (foundNewCar && (car.product_id == 48879 || car.name == "Drive"))
            {
                lstAnki.Add(car);
                if (displayOutput)
                    Console.WriteLine(outputBuffer);
                Console.Write("\r");
                Console.Write("Searching for cars, press any key to stop...  ({0} found)", lstAnki.Count());
            }
        }

        async public static Task<BluetoothLEDevice> Connect(ulong mac_address)
        {
            return await BluetoothLEDevice.FromBluetoothAddressAsync(mac_address);
        }
        public static string GetHexStringFrom(byte[] byteArray)
        {
            return BitConverter.ToString(byteArray);
        }

        // Model 8 = Groundshock, Model 9 = Skull, Model 10 = Thermo, Model 11 = Nuke, Model 12 = Guardian, Model 15 = FreeWheel 
        // Guessing on 13 and 14 below since I do not have those vehicles...
        public enum AnkiModel
        {
            Groundshock = 8, Skull = 9, Thermo = 10, Nuke = 11, Guardian = 12, BigBang = 13, X52 = 14, Freewheel = 15
        }

        public class anki_vehicle
        {
            public uint product_id { get; set; }
            public uint identifier { get; set; }
            public AnkiModel model_id { get; set; }
            public ulong mac_address { get; set; }
            public bool? full_battery { get; set; }
            public bool? low_battery { get; set; }
            public bool? on_charger { get; set; }
            public string name { get; set; }
        }

        public enum AnkiMessage
        {
            // BLE Connections
            ANKI_VEHICLE_MSG_C2V_DISCONNECT = 0x0d,

            // Ping request / response
            ANKI_VEHICLE_MSG_C2V_PING_REQUEST = 0x16,
            ANKI_VEHICLE_MSG_V2C_PING_RESPONSE = 0x17,

            // Messages for checking vehicle version info
            ANKI_VEHICLE_MSG_C2V_VERSION_REQUEST = 0x18,
            ANKI_VEHICLE_MSG_V2C_VERSION_RESPONSE = 0x19,

            // Battery level
            ANKI_VEHICLE_MSG_C2V_BATTERY_LEVEL_REQUEST = 0x1a,
            ANKI_VEHICLE_MSG_V2C_BATTERY_LEVEL_RESPONSE = 0x1b,

            // Lights
            ANKI_VEHICLE_MSG_C2V_SET_LIGHTS = 0x1d,

            // Driving Commands
            ANKI_VEHICLE_MSG_C2V_SET_SPEED = 0x24,
            ANKI_VEHICLE_MSG_C2V_CHANGE_LANE = 0x25,
            ANKI_VEHICLE_MSG_C2V_CANCEL_LANE_CHANGE = 0x26,
            ANKI_VEHICLE_MSG_C2V_TURN = 0x32,

            // Vehicle position updates
            ANKI_VEHICLE_MSG_V2C_LOCALIZATION_POSITION_UPDATE = 0x27,
            ANKI_VEHICLE_MSG_V2C_LOCALIZATION_TRANSITION_UPDATE = 0x29,
            ANKI_VEHICLE_MSG_V2C_LOCALIZATION_INTERSECTION_UPDATE = 0x2a,
            ANKI_VEHICLE_MSG_V2C_VEHICLE_DELOCALIZED = 0x2b,
            ANKI_VEHICLE_MSG_C2V_SET_OFFSET_FROM_ROAD_CENTER = 0x2c,
            ANKI_VEHICLE_MSG_V2C_OFFSET_FROM_ROAD_CENTER_UPDATE = 0x2d,

            // Light Patterns
            ANKI_VEHICLE_MSG_C2V_LIGHTS_PATTERN = 0x33,

            // Vehicle Configuration Parameters
            ANKI_VEHICLE_MSG_C2V_SET_CONFIG_PARAMS = 0x45,

            // SDK Mode
            ANKI_VEHICLE_MSG_C2V_SDK_MODE = 0x90,
        };


        public bool IS_VEHICLE_STATE_SET(int state, int flag) { return ((state) & (flag)) == (flag); }

        [StructLayout(LayoutKind.Sequential)]
        public class anki_vehicle_msg
        {
            public byte size;
            public byte msg_id;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
            public byte[] payload;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class anki_vehicle_msg_version_response
        {
            public byte size;
            public byte msg_id;
            public UInt16 version;
        };
        public static byte ANKI_VEHICLE_MSG_V2C_VERSION_RESPONSE_SIZE = 3;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class anki_vehicle_msg_battery_level_response
        {
            public byte size;
            public byte msg_id;
            public UInt16 battery_level;
        };
        public static byte ANKI_VEHICLE_MSG_V2C_BATTERY_LEVEL_RESPONSE_SIZE = 3;

        public static byte ANKI_VEHICLE_SDK_OPTION_OVERRIDE_LOCALIZATION = 0x1;
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct anki_vehicle_msg_sdk_mode
        {
            public byte size;
            public byte msg_id;
            public byte on;
            public byte flags;
        };
        public static byte ANKI_VEHICLE_MSG_SDK_MODE_SIZE = 3;

        enum anki_vehicle_turn_type
        {
            VEHICLE_TURN_NONE = 0,
            VEHICLE_TURN_LEFT = 1,
            VEHICLE_TURN_RIGHT = 2,
            VEHICLE_TURN_UTURN = 3,
            VEHICLE_TURN_UTURN_JUMP = 4,
        }

        enum anki_vehicle_turn_trigger
        {
            VEHICLE_TURN_TRIGGER_IMMEDIATE = 0, // Run immediately
            VEHICLE_TURN_TRIGGER_INTERSECTION = 1, // Run at the next intersection
        }

        // determine how many bits per code were read
        public static byte PARSEFLAGS_MASK_NUM_BITS = 0x0f;
        // determine if the track has an inverted code scheme
        public static byte PARSEFLAGS_MASK_INVERTED_COLOR = 0x80;
        // determine if the the code has been reverse parsed
        public static byte PARSEFLAGS_MASK_REVERSE_PARSING = 0x40;
        // determine if the current driving dir is reversed
        public static byte PARSEFLAGS_MASK_REVERSE_DRIVING = 0x20;

        public enum anki_vehicle_driving_direction
        {
            FORWARD = 0,
            REVERSE = 1,
        };

        public enum anki_intersection_code
        {
            INTERSECTION_CODE_ENTRY_FIRST,
            INTERSECTION_CODE_EXIT_FIRST,
            INTERSECTION_CODE_ENTRY_SECOND,
            INTERSECTION_CODE_EXIT_SECOND,
        };

        public byte LIGHT_HEADLIGHTS = 0;
        public byte LIGHT_BRAKELIGHTS = 1;
        public byte LIGHT_FRONTLIGHTS = 2;
        public byte LIGHT_ENGINE = 3;

        // Helper macros for parsing lights bits
        //public bool LIGHT_ANKI_VEHICLE_MSG_IS_VALID(messageBits, LIGHT_ID) (((messageBits >> LIGHT_ID)  & 1) == TRUE)
        //public bool LIGHT_ANKI_VEHICLE_MSG_GET_VALUE(messageBits, LIGHT_ID) ((messageBits >> (4 + LIGHT_ANKI_VEHICLE_MSG_HEADLIGHTS) & 1))

        public enum anki_vehicle_light_channel
        {
            LIGHT_RED,
            LIGHT_TAIL,
            LIGHT_BLUE,
            LIGHT_GREEN,
            LIGHT_FRONTL,
            LIGHT_FRONTR,
            LIGHT_COUNT
        };

        public enum anki_vehicle_light_effect
        {
            EFFECT_STEADY,    // Simply set the light intensity to 'start' value
            EFFECT_FADE,      // Fade intensity from 'start' to 'end'
            EFFECT_THROB,     // Fade intensity from 'start' to 'end' and back to 'start'
            EFFECT_FLASH,     // Turn on LED between time 'start' and time 'end' inclusive
            EFFECT_RANDOM,    // Flash the LED erratically - ignoring start/end
            EFFECT_COUNT
        };

        public enum anki_track_material
        {
            TRACK_MATERIAL_PLASTIC,
            TRACK_MATERIAL_VINYL,
        };

        public static byte SUPERCODE_NONE = 0;
        public static byte SUPERCODE_BOOST_JUMP = 1;
        public static byte SUPERCODE_ALL = (SUPERCODE_BOOST_JUMP);
    }

    public class ConsoleSpinner
    {
        int counter;

        public void Turn()
        {
            counter++;
            switch (counter % 4)
            {
                case 0: Console.Write("/"); counter = 0; break;
                case 1: Console.Write("-"); break;
                case 2: Console.Write("\\"); break;
                case 3: Console.Write("|"); break;
            }
            Thread.Sleep(100);
            Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
        }
    }
}
