using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

// Test program for Pulse Train Hat http://www.pthat.com

namespace Pulsetrainjogcontroller
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
        ///

        private SerialDevice serialPort = null;

        private DataWriter dataWriteObject = null;
        private DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        //Gpio Pin Numbers
        private const int Zup_PIN = 26;
        private const int Zdown_PIN = 19;
        private const int Yreverse_PIN = 25;
        private const int Yforward_PIN = 20;
        private const int Xleft_PIN = 16;
        private const int Xright_PIN = 12;

        //Gpio Pin Names
        private GpioPin Zupin;
        private GpioPin Zdpin;
        private GpioPin Xlpin;
        private GpioPin Xrpin;
        private GpioPin Yfpin;
        private GpioPin Yrpin;

        public static class MyStaticValues
        {
            //----Jog status:
            // 0: pressed
            // 1: enabled
            // 2: disabled
            public static int Jenable = 2;

            //Switch case to determine which axis and direction is active
            public static string JogAxis = "";

            //Switch case for whether a button is pressed or released
            public static string JogAction = "";

            //Store Set Axis Commands
            public static string Xsendstore = "";
            public static string Ysendstore = "";
            public static string Zsendstore = "";

            //Stops updating UI with auto pulse replies
            public static int Xcancel_recieve = 0;
            public static int Ycancel_recieve = 0;
            public static int Zcancel_recieve = 0;

            //----Gpio Button State
            // "Floating": Any button is available for use
            // "Internal": Disables Gpio buttons as software buttons are in use
            //    "empty": Buttons are disabled and not available for use
            public static string GL_Press = "Floating";

            //Catches a button release event
            public static int XCatch = 0;
            public static int YCatch = 0;
            public static int ZCatch = 0;

            //Stores Axis Position
            public static string STORETMP = "";
        }

        // initialises the Gpio pins
        private void InitGPIO()  
        {
            //--------------------------------------------------------------------------Z up

            // Assigns the pin number Zup_PIN that was declared above to the pin Zupin
            Zupin = GpioController.GetDefault().OpenPin(Zup_PIN);

            // Check if input pull-up resistors are supported
            if (Zupin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Zupin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Zupin.SetDriveMode(GpioPinDriveMode.Input);
            }
            // Set a debounce timeout to filter out switch bounce noise from a button press
            Zupin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            // Register for the ValueChanged event so our buttonPin_ValueChanged
            // function is called when the button is pressed
            Zupin.ValueChanged += Pin_ValueChanged;

            //------------------------------------------------------------------------Z down
            Zdpin = GpioController.GetDefault().OpenPin(Zdown_PIN);
            if (Zdpin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Zdpin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Zdpin.SetDriveMode(GpioPinDriveMode.Input);
            }
            Zdpin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            Zdpin.ValueChanged += Pin_ValueChanged;

            //----------------------------------------------------------------------Y forward
            Yfpin = GpioController.GetDefault().OpenPin(Yforward_PIN);
            if (Yfpin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Yfpin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Yfpin.SetDriveMode(GpioPinDriveMode.Input);
            }
            Yfpin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            Yfpin.ValueChanged += Pin_ValueChanged;

            //----------------------------------------------------------------------Y reverse
            Yrpin = GpioController.GetDefault().OpenPin(Yreverse_PIN);
            if (Yrpin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Yrpin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Yrpin.SetDriveMode(GpioPinDriveMode.Input);
            }
            Yrpin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            Yrpin.ValueChanged += Pin_ValueChanged;

            //-------------------------------------------------------------------------X left
            Xlpin = GpioController.GetDefault().OpenPin(Xleft_PIN);
            if (Xlpin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Xlpin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Xlpin.SetDriveMode(GpioPinDriveMode.Input);
            }
            Xlpin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            Xlpin.ValueChanged += Pin_ValueChanged;

            //------------------------------------------------------------------------X right
            Xrpin = GpioController.GetDefault().OpenPin(Xright_PIN);
            if (Xrpin.IsDriveModeSupported(GpioPinDriveMode.InputPullDown))
            {
                Xrpin.SetDriveMode(GpioPinDriveMode.InputPullDown);
            }
            else
            {
                Xrpin.SetDriveMode(GpioPinDriveMode.Input);
            }
            Xrpin.DebounceTimeout = TimeSpan.FromMilliseconds(100);
            Xrpin.ValueChanged += Pin_ValueChanged;
        }

        public MainPage()
        {
            this.InitializeComponent();

            //initialise gpios
            InitGPIO();

            //Calls Disable Jog method
            DisableJog();

            comPortInput.IsEnabled = false;
            sendTextButton.IsEnabled = false;

            Reset.IsEnabled = false;

            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>

        // check if switches have been triggered
        private void Pin_ValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                //detects if a pin has gone high
                if (args.Edge == GpioPinEdge.RisingEdge)
                {
                    //checks the pin number
                    if (sender.PinNumber == Zup_PIN)
                    {
                        //checks if nothing has been pressed
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            //sets string to button pressed
                            MyStaticValues.GL_Press = "ZUpressed";

                            //calls press method
                            ZU_PRESS();
                        }
                    }

                    if (sender.PinNumber == Zdown_PIN)
                    {
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            MyStaticValues.GL_Press = "ZDpressed";
                            ZD_PRESS();
                        }
                    }

                    if (sender.PinNumber == Yforward_PIN)
                    {
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            MyStaticValues.GL_Press = "YFpressed";
                            YF_PRESS();
                        }
                    }

                    if (sender.PinNumber == Yreverse_PIN)
                    {
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            MyStaticValues.GL_Press = "YBpressed";
                            YB_PRESS();
                        }
                    }

                    if (sender.PinNumber == Xleft_PIN)
                    {
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            MyStaticValues.GL_Press = "XLpressed";
                            XL_PRESS();
                        }
                    }

                    if (sender.PinNumber == Xright_PIN)
                    {
                        if (MyStaticValues.GL_Press == "Floating")
                        {
                            MyStaticValues.GL_Press = "XRpressed";
                            XR_PRESS();
                        }
                    }
                }

                //detects if a pin has gone low
                if (args.Edge == GpioPinEdge.FallingEdge)
                {
                    if (sender.PinNumber == Zup_PIN)
                    {
                        //checks if this button was previously pressed
                        if (MyStaticValues.GL_Press == "ZUpressed")
                        {
                            //sets value to empty so there is no clash from other buttons being pressed/released
                            MyStaticValues.GL_Press = "empty";
                            //calls release method
                            ZU_RELEASE();
                        }
                    }

                    if (sender.PinNumber == Zdown_PIN)
                    {
                        if (MyStaticValues.GL_Press == "ZDpressed")
                        {
                            MyStaticValues.GL_Press = "empty";
                            ZD_RELEASE();
                        }
                    }

                    if (sender.PinNumber == Yforward_PIN)
                    {
                        if (MyStaticValues.GL_Press == "YFpressed")
                        {
                            MyStaticValues.GL_Press = "empty";
                            YF_RELEASE();
                        }
                    }

                    if (sender.PinNumber == Yreverse_PIN)
                    {
                        if (MyStaticValues.GL_Press == "YBpressed")
                        {
                            MyStaticValues.GL_Press = "empty";
                            YB_RELEASE();
                        }
                    }

                    if (sender.PinNumber == Xleft_PIN)
                    {
                        if (MyStaticValues.GL_Press == "XLpressed")
                        {
                            MyStaticValues.GL_Press = "empty";
                            XL_RELEASE();
                        }
                    }

                    if (sender.PinNumber == Xright_PIN)
                    {
                        if (MyStaticValues.GL_Press == "XRpressed")
                        {
                            MyStaticValues.GL_Press = "empty";
                            XR_RELEASE();
                        }
                    }
                }
            });
        }

        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];

            try
            {
                serialPort = await SerialDevice.FromIdAsync(entry.Id);

                // Disable the 'Connect' button
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(30);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(30);
                if (LowSpeedBaud.IsChecked == true)
                {
                    serialPort.BaudRate = 115200;
                }
                else
                {
                    serialPort.BaudRate = 806400;
                }
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                status.Text = "Serial port configured successfully: ";
                status.Text += serialPort.BaudRate + "-";
                status.Text += serialPort.DataBits + "-";
                status.Text += serialPort.Parity.ToString() + "-";
                status.Text += serialPort.StopBits;

                // Set the RcvdText field to invoke the TextChanged callback
                // The callback launches an async Read task to wait for data
                rcvdText.Text = "Waiting for data...";

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();

                // Enable 'WRITE' button to allow sending data
                sendTextButton.IsEnabled = true;

                Reset.IsEnabled = true;
                EnableJog();

                sendText.Text = "";

                Listen();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
                sendTextButton.IsEnabled = false;

                DisableJog();

                Reset.IsEnabled = false;
            }
        }

        /// <summary>
        /// sendTextButton_Click: Action to take when 'WRITE' button is clicked
        /// - Create a DataWriter object with the OutputStream of the SerialDevice
        /// - Create an async task that performs the write operation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void sendTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "sendTextButton_Click: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync()
        {
            Task<UInt32> storeAsyncTask;

            if (sendText.Text.Length != 0)
            {
                // Load the text from the sendText input text box to the dataWriter object
                dataWriteObject.WriteString(sendText.Text);

                // Launch an async task to complete the write operation
                storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

                UInt32 bytesWritten = await storeAsyncTask;
                if (bytesWritten > 0)
                {
                    status.Text = sendText.Text + ", ";
                    status.Text += "bytes written successfully!";
                }
                //sendText.Text = "";
            }
            else
            {
                status.Text = "Enter the text you want to write and then click on 'WRITE'";
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

                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    status.Text = "Reading task was cancelled, closing device and cleaning up";
                    CloseDevice();
                }
                else
                {
                    status.Text = ex.Message;
                }
            }
            finally
            {
                // Cleanup once complete
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

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            // Launch the task and wait
            UInt32 bytesRead = await loadAsyncTask;

            if (bytesRead > 0)
            {
                rcvdText.Text = dataReaderObject.ReadString(bytesRead);

                string input = rcvdText.Text;
                double Calc;
                //Check if received message can be divided by 7 as our return messages are 7 bytes long
                if (input.Length % 1 == 0)
                {
                    //*********
                    for (int i = 0; i < input.Length; i += 7)
                    {
                        string sub = input.Substring(i, 7);

                        //X Start Recieved
                        if (sub == "RI00SX*")
                        {
                            //X has a caught a button release event
                            if (MyStaticValues.XCatch == 1)
                            {
                                //Disables jog
                                MyStaticValues.Jenable = 2;

                                //Sets action to released
                                MyStaticValues.JogAction = "released";

                                //Initialises jog set method
                                InitJog();
                            }
                            //sets catch to active
                            MyStaticValues.XCatch = 1;
                        }

                        //Y Start Recieved
                        if (sub == "RI00SY*")
                        {
                            if (MyStaticValues.YCatch == 1)
                            {
                                MyStaticValues.Jenable = 2;
                                MyStaticValues.JogAction = "released";
                                InitJog();
                            }
                            MyStaticValues.YCatch = 1;
                        }

                        //Z Start Recieved
                        if (sub == "RI00SZ*")
                        {
                            if (MyStaticValues.ZCatch == 1)
                            {
                                MyStaticValues.Jenable = 2;
                                MyStaticValues.JogAction = "released";
                                InitJog();
                            }
                            MyStaticValues.ZCatch = 1;
                        }

                        //Check if Set X Axis completed
                        if (sub == "CI00CX*")
                        {
                            // Sends out a start X Axis
                            sendText.Text = "I00SX*";
                            SendDataOut();
                        }

                        //Check if Set Y Axis completed
                        if (sub == "CI00CY*")
                        {
                            //sends out a Start Y Axis
                            sendText.Text = "I00SY*";
                            SendDataOut();
                        }

                        //Check if Set Z Axis completed
                        if (sub == "CI00CZ*")
                        {
                            //sends out a Start Z Axis
                            sendText.Text = "I00SZ*";
                            SendDataOut();
                        }

                        //Check if X Axis completed amount of pulses
                        if (sub == "CI00SX*")
                        {
                            //sends out a Get X Pulses
                            sendText.Text = "I00XP*";
                            SendDataOut();
                        }

                        //Check if Y Axis completed amount of pulses
                        if (sub == "CI00SY*")
                        {
                            //sends out a Get Y Pulses
                            sendText.Text = "I00YP*";
                            SendDataOut();
                        }

                        //Check if Z Axis completed amount of pulses
                        if (sub == "CI00SZ*")
                        {
                            //sends out a Get Z Pulses
                            sendText.Text = "I00ZP*";
                            SendDataOut();
                        }

                        //X Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00XP*")
                        {
                            //Multiplies the X pulses by the X resolution to work out our millimeters
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(XResolution.Text);

                            //Updates X Position based on pin direction
                            XPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinX.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //X Request Current Pulse Count Reply Complete
                        if (sub == "CI00XP*")
                        {
                            //Enables jog
                            MyStaticValues.Jenable = 1;

                            //resets X auto pulse reply stop check
                            MyStaticValues.Xcancel_recieve = 0;

                            //resets trigger
                            MyStaticValues.XCatch = 0;

                            //Enables GPIO Buttons
                            MyStaticValues.GL_Press = "Floating";
                        }

                        //Y Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00YP*")
                        {
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(YResolution.Text);
                            YPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinY.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //Y Request Current Pulse Count Reply Complete
                        if (sub == "CI00YP*")
                        {
                            MyStaticValues.Jenable = 1;
                            MyStaticValues.Ycancel_recieve = 0;
                            MyStaticValues.YCatch = 0;
                            MyStaticValues.GL_Press = "Floating";
                        }

                        //Z Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00ZP*")
                        {
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(ZResolution.Text);
                            ZPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinZ.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //Z Request Current Pulse Count Reply Complete
                        if (sub == "CI00ZP*")
                        {
                            MyStaticValues.Jenable = 1;
                            MyStaticValues.Zcancel_recieve = 0;
                            MyStaticValues.ZCatch = 0;
                            MyStaticValues.GL_Press = "Floating";
                        }

                        //Check if Set X Auto Count Feedback
                        if (sub == "CI00JX*")
                        {
                            //Sends Set X Axis
                            sendText.Text = MyStaticValues.Xsendstore;
                            SendDataOut();
                        }

                        //Check if Set Y Auto Count Feedback
                        if (sub == "CI00JY*")
                        {
                            //Sends Set Y Axis
                            sendText.Text = MyStaticValues.Ysendstore;
                            SendDataOut();
                        }

                        //Check if Set Z Auto Count Feedback
                        if (sub == "CI00JZ*")
                        {
                            //Sends Set Z Axis
                            sendText.Text = MyStaticValues.Zsendstore;
                            SendDataOut();
                        }

                        //Check Auto Count X data comes back
                        if (sub == "DI00JX*")
                        {
                            //X Button has not been released
                            if (MyStaticValues.Xcancel_recieve == 0)
                            {
                                //Multiplies the X pulses by the X resolution to work out our millimeters
                                Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(XResolution.Text);

                                //Updates X Position based on pin direction
                                XPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinX.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                            }
                        }

                        //Check Auto Count Y data comes back
                        if (sub == "DI00JY*")
                        {
                            if (MyStaticValues.Ycancel_recieve == 0)
                            {
                                Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(YResolution.Text);
                                YPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinY.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                            }
                        }

                        //Check Auto Count Z data comes back
                        if (sub == "DI00JZ*")
                        {
                            if (MyStaticValues.Zcancel_recieve == 0)
                            {
                                Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(ZResolution.Text);
                                ZPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinZ.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                            }
                        }
                    } // end of for loop
                } //endof checking length if

                status.Text = "bytes read successfully!";
            } //End of checking for bytes
        } //end of async read

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

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;

            comPortInput.IsEnabled = true;
            sendTextButton.IsEnabled = false;
            rcvdText.Text = "";
            listOfDevices.Clear();
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            Disconnectserial();
        }

        private void Disconnectserial()
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
                DisableJog();
                Reset.IsEnabled = false;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        private async void SendDataOut()
        {
            try

            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "Send Data: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "N*";
            SendDataOut();
        }

        //======================================================================================================//
        //-----------------------------------------------Jog Code-----------------------------------------------//
        //======================================================================================================//

        //Y Forward Pressed Method
        private void YF_PRESS()
        {
            //Jog is Enabled
            if (MyStaticValues.Jenable == 1)
            {
                //Set Jog to pressed
                MyStaticValues.Jenable = 0;

                //Determine Axis and Direction
                MyStaticValues.JogAxis = "Yforward";

                //Sets Action as a press
                MyStaticValues.JogAction = "pressed";

                //Resets Catch as it's a press method
                MyStaticValues.YCatch = 0;

                //initialises jog set method
                InitJog();
            }
        }

        //Y Forward Released Method
        private void YF_RELEASE()
        {
            //Y has triggered a release
            if (MyStaticValues.YCatch == 1)
            {
                //Jog is Pressed
                if (MyStaticValues.Jenable == 0)
                {
                    //Jog is Disabled
                    MyStaticValues.Jenable = 2;

                    //Determine Axis and Direction
                    MyStaticValues.JogAxis = "Yforward";

                    //Sets Action as a release
                    MyStaticValues.JogAction = "released";

                    //initialises jog set method
                    InitJog();
                }
            }
            else
            {
                //Set to trigger a release
                MyStaticValues.YCatch = 1;
            }
        }

        //Y Reverse Pressed Method
        private void YB_PRESS()
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Yreverse";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.YCatch = 0;
                InitJog();
            }
        }

        //Y Reverse Released Method
        private void YB_RELEASE()
        {
            if (MyStaticValues.YCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Yreverse";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.YCatch = 1;
            }
        }

        //X Right Pressed Method
        private void XR_PRESS()
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Xright";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.XCatch = 0;
                InitJog();
            }
        }

        //X Right Released Method
        private void XR_RELEASE()
        {
            if (MyStaticValues.XCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Xright";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.XCatch = 1;
            }
        }

        //X Left Pressed Method
        private void XL_PRESS()
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Xleft";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.XCatch = 0;
                InitJog();
            }
        }

        //X Left Released Method
        private void XL_RELEASE()
        {
            if (MyStaticValues.XCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Xleft";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.XCatch = 1;
            }
        }

        //Z Up Pressed Method
        private void ZU_PRESS()
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Zup";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.ZCatch = 0;
                InitJog();
            }
        }

        //Z Up Released Method
        private void ZU_RELEASE()
        {
            if (MyStaticValues.ZCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Zup";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.ZCatch = 1;
            }
        }

        //Z Down Pressed Method
        private void ZD_PRESS()
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Zdown";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.ZCatch = 0;
                InitJog();
            }
        }

        //Z Down Released Method
        private void ZD_RELEASE()
        {
            if (MyStaticValues.ZCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Zdown";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.ZCatch = 1;
            }
        }

        //Jog Y Forward has been pressed
        private void Jyf_press(object sender, PointerRoutedEventArgs e)
        {
            //Checks if any GPIO Pins are high
            if (MyStaticValues.GL_Press == "Floating")
            {
                //Prevents the use of GPIO inputs
                MyStaticValues.GL_Press = "Internal";

                //Calls Y Forward Pressed Method
                YF_PRESS();
            }
        }

        //Jog Y Forward has been released
        private void Jyf_release(object sender, PointerRoutedEventArgs e)
        {
            //Checks if a press method has been passed
            if (MyStaticValues.GL_Press == "Internal")
            {
                //Calls Y Forward Release Method
                YF_RELEASE();
            }
        }

        //Jog Y Reverse has been pressed
        private void Jyb_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Floating")
            {
                MyStaticValues.GL_Press = "Internal";
                YB_PRESS();
            }
        }

        //Jog Y Reverse has been released
        private void Jyb_release(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Internal")
            {
                YB_RELEASE();
            }
        }

        //Jog X Right has been pressed
        private void Jxr_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Floating")
            {
                MyStaticValues.GL_Press = "Internal";
                XR_PRESS();
            }
        }

        //Jog X Right has been released
        private void Jxr_release(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Internal")
            {
                XR_RELEASE();
            }
        }

        //Jog X Left has been pressed
        private void Jxl_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Floating")
            {
                MyStaticValues.GL_Press = "Internal";
                XL_PRESS();
            }
        }

        //Jog X Left has been released
        private void Jxl_release(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Internal")
            {
                XL_RELEASE();
            }
        }

        //Jog Z Up has been pressed
        private void Jzu_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Floating")
            {
                MyStaticValues.GL_Press = "Internal";
                ZU_PRESS();
            }
        }

        //Jog Z Up has been released
        private void Jzu_release(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Internal")
            {
                ZU_RELEASE();
            }
        }

        //Jog Z Down has been pressed
        private void Jzd_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Floating")
            {
                MyStaticValues.GL_Press = "Internal";
                ZD_PRESS();
            }
        }

        //Jog Z Down has been released
        private void Jzd_release(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.GL_Press == "Internal")
            {
                ZD_RELEASE();
            }
        }

        //Mouse has exited X Left Jog button
        private void Jxl_Exit(object sender, PointerRoutedEventArgs e)
        {
            //Jog has been pressed
            if (MyStaticValues.Jenable == 0)
            {
                //calls X Left release method
                XL_RELEASE();
            }
        }

        //Mouse has exited X Right Jog button
        private void Jxr_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                XR_RELEASE();
            }
        }

        //Mouse has exited Y Forward Jog button
        private void Jyf_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                YF_RELEASE();
            }
        }

        //Mouse has exited Y Reverse Jog button
        private void Jyb_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                YB_RELEASE();
            }
        }

        //Mouse has exited Z Up Jog button
        private void Jzu_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                ZU_RELEASE();
            }
        }

        //Mouse has exited Z Down Jog button
        private void Jzd_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                ZD_RELEASE();
            }
        }

        //Method that initialises Jogging
        private void InitJog()
        {
            //Axis Direction
            string Dir = "";

            //Jog Axis
            string JogIndicator = "";
            
            //Jog Speed (Hertz)
            string JogSpeed = "";
            
            //Pulse Count for Auto Pulse Reply Interval
            string AutoPulseReply = "";
            
            //Which Axis is enabled for pulse count
            string AutoPulseEnable = "";

            //determines whether the button is pressed or released
            switch (MyStaticValues.JogAction)
            {
                //Button is pressed
                case "pressed":

                    //determines which axis and direction to start
                    switch (MyStaticValues.JogAxis)
                    {
                        case "Yforward":

                            //sets image to Button Down
                            BitmapImage j = new BitmapImage(new Uri("ms-appx:///Assets/Up_D.png"));
                            Jyf.Source = j;

                            //sets the axis indicator
                            JogIndicator = "Y";
                            
                            //sets the jog speed for the axis
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(YHZresult.Text));
                            
                            //Store Auto Pulse Reply Interval
                            AutoPulseReply = YHZresult.Text;
                            
                            //Enables Y axis Auto Pulse Reply
                            AutoPulseEnable = "0100";
                            
                            //sets the pin direction
                            Dir = PinY.Text;

                            break;

                        case "Yreverse":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down_D.png"));
                            Jyb.Source = j;
                            JogIndicator = "Y";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(YHZresult.Text));
                            AutoPulseEnable = "0100";
                            AutoPulseReply = YHZresult.Text;
                            Dir = (PinY.Text == "1") ? "0" : "1";

                            break;

                        case "Xleft":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Left_D.png"));
                            Jxl.Source = j;
                            JogIndicator = "X";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(XHZresult.Text));
                            AutoPulseReply = XHZresult.Text;
                            AutoPulseEnable = "1000";
                            Dir = (PinX.Text == "1") ? "0" : "1";

                            break;

                        case "Xright":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Right_D.png"));
                            Jxr.Source = j;
                            JogIndicator = "X";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(XHZresult.Text));
                            AutoPulseReply = XHZresult.Text;
                            AutoPulseEnable = "1000";
                            Dir = PinX.Text;

                            break;

                        case "Zup":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Up_D.png"));
                            Jzu.Source = j;
                            JogIndicator = "Z";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(ZHZresult.Text));
                            AutoPulseEnable = "0010";
                            AutoPulseReply = ZHZresult.Text;
                            Dir = PinX.Text;

                            break;

                        case "Zdown":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down_D.png"));
                            Jzd.Source = j;
                            JogIndicator = "Z";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(ZHZresult.Text));
                            AutoPulseReply = ZHZresult.Text;
                            AutoPulseEnable = "0010";
                            Dir = (PinZ.Text == "1") ? "0" : "1";

                            break;
                    }

                    //Determines which axis is selected
                    switch (JogIndicator)
                    {
                        case "X":
                            //stores Xposition
                            MyStaticValues.STORETMP = XPosition.Text;

                            //stores send command
                            MyStaticValues.Xsendstore = "I00C" + JogIndicator + JogSpeed + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(Jog_RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(Jog_RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";

                            break;

                        case "Y":
                            MyStaticValues.STORETMP = YPosition.Text;
                            MyStaticValues.Ysendstore = "I00C" + JogIndicator + JogSpeed + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(Jog_RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(Jog_RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";

                            break;

                        case "Z":
                            MyStaticValues.STORETMP = ZPosition.Text;
                            MyStaticValues.Zsendstore = "I00C" + JogIndicator + JogSpeed + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(Jog_RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(Jog_RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";

                            break;
                    }

                    //Calculate Auto Pulse Reply Interval, dividing by 4 means we will get a reply every 250ms after ramping
                    AutoPulseReply = String.Format("{0:0000000000}", (Math.Floor(Convert.ToDouble(AutoPulseReply) / 4)));

                    //Sets Auto Pulse Reply
                    sendText.Text = "I00J" + JogIndicator + AutoPulseReply + AutoPulseEnable + "*";
                    SendDataOut();

                    break;

                // determines that the button is released
                case "released":

                    //determines which axis and direction to stop
                    switch (MyStaticValues.JogAxis)
                    {
                        case "Yforward":

                            //sets the image to button released
                            BitmapImage j = new BitmapImage(new Uri("ms-appx:///Assets/Up.png"));
                            Jyf.Source = j;

                            //Sets the release command to be triggered
                            MyStaticValues.Ycancel_recieve = 1;

                            //sends out a stop axis command
                            sendText.Text = "I00TY*";
                            SendDataOut();

                            break;

                        case "Yreverse":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down.png"));
                            Jyb.Source = j;
                            sendText.Text = "I00TY*";
                            MyStaticValues.Ycancel_recieve = 1;
                            SendDataOut();

                            break;

                        case "Xleft":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Left.png"));
                            Jxl.Source = j;
                            MyStaticValues.Xcancel_recieve = 1;
                            sendText.Text = "I00TX*";
                            SendDataOut();

                            break;

                        case "Xright":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Right.png"));
                            Jxr.Source = j;
                            MyStaticValues.Xcancel_recieve = 1;
                            sendText.Text = "I00TX*";
                            SendDataOut();

                            break;

                        case "Zup":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Up.png"));
                            Jzu.Source = j;
                            sendText.Text = "I00TZ*";
                            MyStaticValues.Zcancel_recieve = 1;
                            SendDataOut();

                            break;

                        case "Zdown":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down.png"));
                            Jzd.Source = j;
                            sendText.Text = "I00TZ*";
                            MyStaticValues.Zcancel_recieve = 1;
                            SendDataOut();

                            break;
                    }

                    break;
            }
        }

        private void XmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(XmmMIN.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void YmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(YmmMIN.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void ZmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(ZmmMIN.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void EmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(EmmMIN.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void XStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(XStepsPerMM.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void YStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(YStepsPerMM.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void ZStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(ZStepsPerMM.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void EStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(EStepsPerMM.Text.Trim()))
            {
            }
            else
            {
                calculatetravelspeeds();
            }
        }

        private void calculatetravelspeeds()
        {
            XHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(XmmMIN.Text) / 60) * Convert.ToDouble(XStepsPerMM.Text));
            XResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(XStepsPerMM.Text));

            YHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(YmmMIN.Text) / 60) * Convert.ToDouble(YStepsPerMM.Text));
            YResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(YStepsPerMM.Text));

            ZHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(ZmmMIN.Text) / 60) * Convert.ToDouble(ZStepsPerMM.Text));
            ZResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(ZStepsPerMM.Text));

            EHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(EmmMIN.Text) / 60) * Convert.ToDouble(EStepsPerMM.Text));
            EResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(EStepsPerMM.Text));
        }

        private void ResetX_Click(object sender, RoutedEventArgs e)
        {
            XPosition.Text = "0000.00000";
        }

        private void ResetZ_Click(object sender, RoutedEventArgs e)
        {
            ZPosition.Text = "0000.00000";
        }

        private void ResetY_Click(object sender, RoutedEventArgs e)
        {
            YPosition.Text = "0000.00000";
        }

        private void ResetE_Click(object sender, RoutedEventArgs e)
        {
            EPosition.Text = "0000.00000";
        }

        //Disables Jog and resets variables
        private void DisableJog()
        {
            MyStaticValues.Jenable = 2;
            MyStaticValues.XCatch = 0;
            MyStaticValues.YCatch = 0;
            MyStaticValues.ZCatch = 0;
            MyStaticValues.Xcancel_recieve = 0;
            MyStaticValues.Ycancel_recieve = 0;
            MyStaticValues.Zcancel_recieve = 0;
        }

        //Enables Jog and resets variables
        private void EnableJog()
        {
            MyStaticValues.Jenable = 1;
            MyStaticValues.XCatch = 0;
            MyStaticValues.YCatch = 0;
            MyStaticValues.ZCatch = 0;
            MyStaticValues.Xcancel_recieve = 0;
            MyStaticValues.Ycancel_recieve = 0;
            MyStaticValues.Zcancel_recieve = 0;
        }
    }
}