using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Media.Capture;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using Windows.System.Display;
using Windows.Graphics.Display;
using Windows.UI.Core;
using Windows.Devices.Enumeration;
using System.Diagnostics;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CameraAppForDeviceDemo
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Camera preview related.
        private MediaCapture MediaCapture { get; set; }
        private bool IsMediaCaptureInitialized { get; set; }
        private bool IsMediaCapturePreviewing { get; set; }

        // UI state related.
        private bool IsSuspending { get; set; }
        private bool IsActivePage { get; set; }

        private bool IsUiActive { get; set; }
        private Task SetupTask { get; set; }

        private int CurrentRotateDegree { get; set; }

        private DisplayRequest DisplayRequest { get; set; }

        public MainPage()
        {
            this.InitializeComponent();

            // Do not cache the state of the UI when suspending/navigating
            NavigationCacheMode = NavigationCacheMode.Disabled;

            MediaCapture = null;
            IsMediaCaptureInitialized = false;
            IsMediaCapturePreviewing = false;

            IsSuspending = false;
            IsActivePage = false;

            IsUiActive = false;
            SetupTask = Task.CompletedTask;

            CurrentRotateDegree = 0;

            DisplayRequest = new DisplayRequest();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            Debug.WriteLine("OnNavigatedTo()");

            Application.Current.Suspending += Application_Suspending;
            Application.Current.Resuming += Application_Resuming;
            Window.Current.VisibilityChanged += Window_VisibilityChanged;

            IsActivePage = true;
            await SetupBasedOnStateAsync();
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            Debug.WriteLine("OnNavigatedFrom()");

            Application.Current.Suspending -= Application_Suspending;
            Application.Current.Resuming -= Application_Resuming;
            Window.Current.VisibilityChanged -= Window_VisibilityChanged;

            IsActivePage = false;
            await SetupBasedOnStateAsync();
        }

        private void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            Debug.WriteLine("State Changing: Suspending");

            IsSuspending = true;

            var deferral = e.SuspendingOperation.GetDeferral();
            var task = Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
            {
                await SetupBasedOnStateAsync();
                deferral.Complete();
            });
        }

        private void Application_Resuming(object sender, object e)
        {
            Debug.WriteLine("State Changing: Resuming");

            IsSuspending = false;

            var task = Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
            {
                await SetupBasedOnStateAsync();
            });
        }

        private async void Window_VisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            Debug.WriteLine("Window_VisibilityChanged");

            await SetupBasedOnStateAsync();
        }

        private async Task SetupBasedOnStateAsync()
        {
            Debug.WriteLine("SetupBasedOnStateAsync");

            // Avoid reentrancy: Wait until nobody else is in this function.
            while (!SetupTask.IsCompleted)
            {
                Debug.WriteLine("Wait setup task");
                await SetupTask;
            }

            // We wont UI to be active if:
            //   - We are the current active page.
            //   - The window is visible.
            //   - The app is not suspending.
            var wantUiActive = IsActivePage && Window.Current.Visible && !IsSuspending;

            if (IsUiActive != wantUiActive)
            {
                IsUiActive = wantUiActive;

                async Task setupAsync()
                {
                    if (wantUiActive)
                    {
                        await InitializeCameraAsync();
                    }
                    else
                    {
                        await CleanupCameraAsync();
                    }
                }

                SetupTask = setupAsync();
            }

            await SetupTask;
        }

        private async Task InitializeCameraAsync()
        {
            Debug.WriteLine("InitializeCameraAsync()");

            if (MediaCapture == null)
            {
                // Find specified camera.
                //var cameraDeviceName = "Integrated Camera";
                var cameraDeviceName = "C922 Pro Stream Webcam";
                var cameraDevice = await FindCameraDeviceByDeviceNameAsync(cameraDeviceName);
                if (cameraDevice == null)
                {
                    await ShowMessageToUser("Camera device not found", string.Format("Couldn't find Camera device '{0}'.", cameraDeviceName));
                    return;
                }

                MediaCapture = new MediaCapture();

                try
                {
                    // Initialize MediaCapture.
                    var mediaInitSettings = new MediaCaptureInitializationSettings
                    {
                        VideoDeviceId = cameraDevice.Id,
                        StreamingCaptureMode = StreamingCaptureMode.Video,
                        MediaCategory = MediaCategory.Media,
                    };
                    await MediaCapture.InitializeAsync(mediaInitSettings);

                    IsMediaCaptureInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine("Couldn't initialize media capture. The app was denied access to the camera.");
                }

                // Focus
                InitializeFocusSetting();

                // Zoom
                InitializeZoomSetting();

                if (IsMediaCaptureInitialized)
                {
                    await StartPreviewAsync();
                }
                else
                {
                    await ShowMessageToUser("UnauthorizedAccessException", "Couldn't initialize media capture. The app was denied access to the camera.");
                }
            }
        }

        private async Task<DeviceInformation> FindCameraDeviceByDeviceNameAsync(string deviceName)
        {
            Debug.WriteLine("Find the device: {0}", new object[] { deviceName });

            // Finds all video capture devices.
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            foreach (var device in devices)
            {
                Debug.WriteLine("MediaCapture.IsVideoProfileSupported(device.Id): {0}", MediaCapture.IsVideoProfileSupported(device.Id));
                Debug.WriteLine("deviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase): {0}", deviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));

                // Check if the device has the requested device name.
                if (deviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            return null;
        }

        private void InitializeFocusSetting()
        {
            var focus = MediaCapture.VideoDeviceController.Focus;
            if (focus.Capabilities.Supported)
            {
                Debug.WriteLine("Focus control supported");

                if (focus.TrySetAuto(false))
                {
                    Debug.WriteLine("Disable auto focus");

                    FocusSlider.Minimum = focus.Capabilities.Min;
                    FocusSlider.Maximum = focus.Capabilities.Max;
                    FocusSlider.StepFrequency = focus.Capabilities.Step;

                    FocusSlider.ValueChanged -= FocusSlider_ValueChanged;

                    if (focus.TryGetValue(out double focusValue))
                    {
                        Debug.WriteLine("Set value");
                        FocusSlider.Value = focusValue;
                    }
                    else
                    {
                        Debug.WriteLine("Failed set value");
                        FocusSlider.Value = FocusSlider.Minimum;
                    }

                    FocusSlider.ValueChanged += FocusSlider_ValueChanged;
                }
            }
            else
            {
                Debug.WriteLine("Focus control NOT supported");
            }
        }

        private void FocusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var focus = MediaCapture.VideoDeviceController.Focus;
            if (focus.TrySetValue(e.NewValue))
            {
                Debug.WriteLine("Focus changed");
            }
            else
            {
                Debug.WriteLine("Failed focus changed");
            }
        }

        private void InitializeZoomSetting()
        {
            var zoom = MediaCapture.VideoDeviceController.Zoom;
            if (zoom.Capabilities.Supported)
            {
                Debug.WriteLine("Zoom control supported");

                ZoomSlider.Minimum = zoom.Capabilities.Min;
                ZoomSlider.Maximum = zoom.Capabilities.Max;
                ZoomSlider.StepFrequency = zoom.Capabilities.Step;

                ZoomSlider.ValueChanged -= ZoomSlider_ValueChanged;

                if (zoom.TryGetValue(out double zoomValue))
                {
                    Debug.WriteLine("Set value");
                    ZoomSlider.Value = zoomValue;
                }
                else
                {
                    Debug.WriteLine("Failed set value");
                    ZoomSlider.Value = FocusSlider.Minimum;
                }

                ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            }
            else
            {
                Debug.WriteLine("Zoom control NOT supported");
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var zoom = MediaCapture.VideoDeviceController.Zoom;
            if (zoom.TrySetValue(e.NewValue))
            {
                Debug.WriteLine("Zoom changed");
            }
            else
            {
                Debug.WriteLine("Failed zoom changed");
            }
        }

        private async Task StartPreviewAsync()
        {
            Debug.WriteLine("StartPreviewAsync()");

            // Prevent the device going to sleep while the previewing.
            DisplayRequest.RequestActive();
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;

            try
            {
                // Start the preview.
                CameraPreview.Source = MediaCapture;
                await MediaCapture.StartPreviewAsync();

                IsMediaCapturePreviewing = true;
            }
            catch (FileLoadException)
            {
                MediaCapture.CaptureDeviceExclusiveControlStatusChanged += MediaCapture_CaptureDeviceExclusiveControlStatusChanged;
            }

            if (IsMediaCapturePreviewing)
            {
                // Rotate video preview.
                await SetPreviewRotationAsync(CurrentRotateDegree);
            }
        }

        // Rotation metadata to apply to the preview stream and recorded videos (MF_MT_VIDEO_ROTATION)
        // Reference: http://msdn.microsoft.com/en-us/library/windows/apps/xaml/hh868174.aspx
        private static readonly Guid VideoRotationGuid = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");

        private async Task SetPreviewRotationAsync(int degree)
        {
            var videoPreviewProperties = MediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            videoPreviewProperties.Properties.Add(VideoRotationGuid, degree);
            await MediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, videoPreviewProperties, null);
        }

        private async void MediaCapture_CaptureDeviceExclusiveControlStatusChanged(MediaCapture sender, MediaCaptureDeviceExclusiveControlStatusChangedEventArgs args)
        {
            if (args.Status == MediaCaptureDeviceExclusiveControlStatus.SharedReadOnlyAvailable)
            {
                await ShowMessageToUser("CaptureDeviceExclusiveControlStatusChanged", "SharedReadOnlyAvailable");
            }
            else if (args.Status == MediaCaptureDeviceExclusiveControlStatus.ExclusiveControlAvailable && !IsMediaCapturePreviewing)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await StartPreviewAsync();
                });
            }
        }

        private async Task CleanupCameraAsync()
        {
            Debug.WriteLine("CleanupCameraAsync()");

            if (IsMediaCaptureInitialized)
            {
                if (IsMediaCapturePreviewing)
                {
                    await StopPreviewAsync();
                }

                IsMediaCaptureInitialized = false;
            }

            if (MediaCapture != null)
            {
                MediaCapture.Dispose();
                MediaCapture = null;
            }
        }

        private async Task StopPreviewAsync()
        {
            Debug.WriteLine("StopPreviewAsync()");

            if (IsMediaCapturePreviewing)
            {
                // Stop the preview.
                await MediaCapture?.StopPreviewAsync();
                IsMediaCapturePreviewing = false;
            }

            // Use the dispatcher because this method is sometimes called from non-UI threads.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Cleanup the UI.
                CameraPreview.Source = null;

                // Allow device going to sleep.
                DisplayRequest?.RequestRelease();
            });
        }

        private async void RotateButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentRotateDegree = (CurrentRotateDegree + 90) % 360;

            if (IsMediaCapturePreviewing)
            {
                // Rotate video preview.
                await SetPreviewRotationAsync(CurrentRotateDegree);
            }
        }

        private async Task ShowMessageToUser(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "OK"
            };

            await dialog.ShowAsync();
        }
    }
}
