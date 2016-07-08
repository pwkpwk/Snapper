namespace Snapper
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;
    using Windows.Graphics.Imaging;
    using Windows.Media.Capture;
    using Windows.Media.Editing;
    using Windows.Media.MediaProperties;
    using Windows.Media.Transcoding;
    using Windows.Storage;
    using Windows.Storage.Streams;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Imaging;
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly MediaCapture _mediaCapture;
        private readonly LowLagPhotoCapture _capture;
        private readonly DispatcherTimer _timer;
        private readonly MediaComposition _composition;
        private int _frameNumber;
        private bool _finished;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += this.OnLoaded;
            _mediaCapture = new MediaCapture();
            _mediaCapture.InitializeAsync().AsTask().Wait();

            ImageEncodingProperties props = ImageEncodingProperties.CreateBmp();
            props.Width = 1024;
            props.Height = 768;
            Task<LowLagPhotoCapture> task = _mediaCapture.PrepareLowLagPhotoCaptureAsync(props).AsTask();
            _capture = task.Result;

            _timer = new DispatcherTimer();

            _composition = new MediaComposition();

            _frameNumber = 1;
            _finished = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.CapturePreview.Source = _mediaCapture;
            //_mediaCapture.StartPreviewAsync().AsTask();
            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += this.OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object sender, object e)
        {
            DispatcherTimer timer = (DispatcherTimer)sender;

            timer.Stop();

            CapturedPhoto photo = _capture.CaptureAsync().AsTask().Result;
            WriteableBitmap bitmap = new WriteableBitmap((int)photo.Frame.Width, (int)photo.Frame.Height);
            bitmap.SetSource(photo.Frame);

            this.CapturedImage.Source = bitmap;

            StorageFile file = ApplicationData.Current.TemporaryFolder.CreateFileAsync(string.Format("frame-{0}.jpg", (_frameNumber++).ToString("D8")), CreationCollisionOption.ReplaceExisting).AsTask().Result;
            IRandomAccessStream encodedStream = file.OpenAsync(FileAccessMode.ReadWrite).AsTask().Result;

            BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encodedStream).AsTask().ContinueWith(t =>
            {
                BitmapEncoder encoder = t.Result;

                this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    using (Stream pixelStream = bitmap.PixelBuffer.AsStream())
                    {
                        byte[] pixels = new byte[pixelStream.Length];
                        pixelStream.Read(pixels, 0, pixels.Length);
                        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96.0, 96.0, pixels);
                    }

                    encoder.FlushAsync().AsTask().ContinueWith(et =>
                    {
                        encodedStream.Dispose();
                        MediaClip clip = MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(1.0 / 30.0)).AsTask().Result;
                        _composition.Clips.Add(clip);

                        this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if(!_finished)
                                timer.Start();
                        }).AsTask();
                    });
                }).AsTask();
            });
        }

        private void OnRenderClicked(object sender, RoutedEventArgs e)
        {
            _finished = true;
            _timer.Stop();
            _capture.FinishAsync().AsTask().Wait();

            MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
            profile.Video.Width = 1024;
            profile.Video.Height = 720;
            profile.Video.FrameRate.Numerator = 30;
            profile.Video.FrameRate.Denominator = 1;

            StorageLibrary library = StorageLibrary.GetLibraryAsync(KnownLibraryId.Videos).AsTask().Result;
            StorageFile file = library.SaveFolder.CreateFileAsync("timelapse.mp4", CreationCollisionOption.ReplaceExisting).AsTask().Result;

            TranscodeFailureReason reason = _composition.RenderToFileAsync(file).AsTask().Result;
        }
    }
}
