using System.IO;
using System.Linq;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.ConstraintLayout.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Lang;
using Java.Nio;
using Java.Util;
using Java.Util.Concurrent;
using Xamarin.TensorFlow.Lite;
using Xamarin.TensorFlow.Lite.Nnapi;
using Org.Tensorflow.Lite.Support.Common.Ops;
using Org.Tensorflow.Lite.Support.Image;
using Org.Tensorflow.Lite.Support.Image.Ops;
using Org.Tensorflow.Lite.Support.Common;
using Org.Tensorflow.Lite.Support.Metadata;



namespace CamApp2
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity,
        PixelCopy.IOnPixelCopyFinishedListener,
        ImageAnalysis.IAnalyzer
    {
        private ConstraintLayout container;
        private Bitmap bitmapBuffer;

        private IExecutorService executor = Executors.NewSingleThreadExecutor();
        // private string[] permissions = { Manifest.Permission.Camera };
        // private int permissionsRequestCode = new System.Randome().Next(0, 10000);

        private int lensFacing = CameraSelector.LensFacingBack;
        private bool isFrontFacing() {  return lensFacing == CameraSelector.LensFacingFront; }

        private bool pauseAnalysis = false;
        private int imageRotationDegrees = 0;
        private TensorImage tfImageBuffer = new TensorImage(Xamarin.TensorFlow.Lite.DataType.Uint8);

        private ImageProcessor tfImageProcessor;
        private NnApiDelegate nnApiDelegate;
        private Interpreter tflite;
        private ObjectDetectionHelper detector;
        private Size tfInputSize;
        private const float AccuracyThreshold = 0.5f;
        private const string ModelPath = "model.tflite";


        private const string TAG = "CameraXBasic";
        private const int REQUEST_CODE_PERMISSIONS = 10;
        private const string FILENAME_FORMAT = "yyyy-MM-dd-HH-mm-ss-SSS";

        ImageCapture imageCapture;
        Java.IO.File outputDirectory;
        IExecutor cameraExecutor;

        private int frameCounter;
        private long lastFpsTimestamp;
        private SurfaceView surfaceView;
        private TextureView textureView;


        PreviewView viewFinder;
        private SkiaSharp.Views.Android.SKCanvasView canvasView;


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
            container = FindViewById(Resource.Id.camera_container) as ConstraintLayout;
            this.viewFinder = this.FindViewById<PreviewView>(Resource.Id.viewFinder);
            var camera_capture_button = this.FindViewById<Button>(Resource.Id.camera_capture_button);
            //this.canvasView = this.FindViewById<SkiaSharp.Views.Android.SKCanvasView>(Resource.Id.CanvasView);

            // ~~~~~~~~~~~~~~~~~~~~~~~~
            // ML model variables
            // ~~~~~~~~~~~~~~~~~~~~~~~~
            
            ByteBuffer tfliteModel = FileUtil.LoadMappedFile(this, ModelPath );
            nnApiDelegate = new NnApiDelegate();
            tflite = new Interpreter(tfliteModel, new Interpreter.Options().AddDelegate(nnApiDelegate));
            MetadataExtractor metadataExtractor = new MetadataExtractor(tfliteModel);
            // Stream labelFile = metadataExtractor.GetAssociatedFile("labelmap.txt");
            JavaList<string> labelList = new JavaList<string>();
            labelList.Add("cat");
            labelList.Add("dog");
            labelList.Add("???");
            detector = new ObjectDetectionHelper(tflite, labelList);
            var inputIndex = 0;
            var inputShape = metadataExtractor.GetInputTensorShape(inputIndex);
            tfInputSize = new Size(inputShape[2], inputShape[1]); // Order of Axis is: 1, height, width, 3

            // Request camera permissions
            string[] permissions = new string[] { Manifest.Permission.Camera, Manifest.Permission.WriteExternalStorage };
            if (permissions.FirstOrDefault(x => ContextCompat.CheckSelfPermission(this, x) != Android.Content.PM.Permission.Granted) != null)
            {
                Toast.MakeText(this.BaseContext, "Hello :)", ToastLength.Short).Show();
                ActivityCompat.RequestPermissions(this, permissions, REQUEST_CODE_PERMISSIONS);
                ActivityCompat.RequestPermissions(this, permissions, REQUEST_CODE_PERMISSIONS);
            }
            else
                StartCamera();

            // Set up the listener for take photo button
            camera_capture_button.SetOnClickListener(new OnClickListener(() => OnClick()));

            outputDirectory = GetOutputDirectory();

            cameraExecutor = Executors.NewSingleThreadExecutor();
        }

        protected override void OnDestroy()
        {
            nnApiDelegate?.Close();
            base.OnDestroy();
            cameraExecutor.Dispose();
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            if (requestCode == REQUEST_CODE_PERMISSIONS)
            {
                if (permissions.FirstOrDefault(x => ContextCompat.CheckSelfPermission(this, x) != Android.Content.PM.Permission.Granted) == null)
                {
                    StartCamera();
                }
                else
                {
                    Toast.MakeText(this, "Permissions not Granted by the user.", ToastLength.Short).Show();
                    this.Finish();
                    return;
                }

            }
            /*
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            */
        }

        public void OnClick()
        {
            // Disable all Camera Controls
            var v = FindViewById<Button>(Resource.Id.camera_capture_button);
            v.Enabled = false;

            ImageView imagePredicted = FindViewById(Resource.Id.image_predicted) as ImageView;
            if (pauseAnalysis)
            {
                // If image analysis is in paused state, resume it
                pauseAnalysis = false;
                imagePredicted.Visibility = ViewStates.Gone;
            }
            else
            {
                // Otherwise, pause image analysis and freeze image
                pauseAnalysis = true;
                var matrix = new Matrix();
                matrix.PostRotate((float)imageRotationDegrees);
                var uprightImage = Bitmap.CreateBitmap(
                    bitmapBuffer, 0, 0, bitmapBuffer.Width, bitmapBuffer.Height, matrix, false);
                imagePredicted.SetImageBitmap(uprightImage);
                imagePredicted.Visibility = ViewStates.Visible;
            }

            // Re-enable camera controls
            v.Enabled = true;
        }

        private void StartCamera()
        {
           // canvasView.PaintSurface += OnPaintSurface;

            var CameraProviderFuture = ProcessCameraProvider.GetInstance(this);

            viewFinder.Post(() =>
            {
                CameraProviderFuture.AddListener(new Runnable(() =>
                {
                // Binds the lifecycle of cameras to the lifecycle owner
                var cameraProvider = (ProcessCameraProvider)CameraProviderFuture.Get();

                // Preview
                var preview = new Preview.Builder().Build();
                    preview.SetSurfaceProvider(viewFinder.SurfaceProvider);

                    
                    //Take Photo
                this.imageCapture = new ImageCapture.Builder().Build();

                /*
                // Frame by Frame analyze
                var imageAnalyzer = new ImageAnalysis.Builder().Build();
                imageAnalyzer.SetAnalyzer(cameraExecutor, new LuminosityAnalyzer(luma =>
                    Log.Debug(TAG, $"Average luminosity: {luma}")
                    ));
                */
                    Log.Debug(TAG, this.viewFinder.Display.Rotation.ToString());
                    var imageAnalysis = new ImageAnalysis.Builder()
                        .SetTargetAspectRatio(AspectRatio.Ratio43)
                        .SetTargetRotation((int)this.viewFinder.Display.Rotation)
                        .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest).Build();

                    frameCounter = 0;
                    lastFpsTimestamp = JavaSystem.CurrentTimeMillis();

                    imageAnalysis.SetAnalyzer(cameraExecutor, this);

                // Select Front Camera as default
                CameraSelector cameraSelector = new CameraSelector.Builder().RequireLensFacing(lensFacing).Build();

                    try
                    {
                    // Unbind use cases before rebinding
                    cameraProvider.UnbindAll();

                    // Bind use cases to camera
                    cameraProvider.BindToLifecycle(this, cameraSelector, preview, imageAnalysis);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(TAG, "Use Case Binding Failed", ex);
                        Toast.MakeText(this, $"Use case binding failed: {ex.Message}", ToastLength.Short).Show();

                    }

                // Use the camera object to link our preview use case with the view
                preview.SetSurfaceProvider(viewFinder.SurfaceProvider);
                    OnPreviewSizeChosen(preview.AttachedSurfaceResolution);

                    viewFinder.Post(() =>
                    {
                        surfaceView = (SurfaceView)viewFinder.GetChildAt(0);
                        textureView = viewFinder.GetChildAt(0) as TextureView;
                    });

                }), ContextCompat.GetMainExecutor(this)); // returns an executor that runs on the main thread
            });
        }

        public void OnPixelCopyFinished(int copyResult)
        {
            if (copyResult != (int)PixelCopyResult.Success)
                Log.Error(TAG, "OnPixelCopyFinished() failed");
        }

        private void OnPreviewSizeChosen(Size size)
        {
            imageRotationDegrees = viewFinder.Display.Rotation switch
            {
                SurfaceOrientation.Rotation0 => 0,
                SurfaceOrientation.Rotation90 => 270,
                SurfaceOrientation.Rotation180 => 180,
                SurfaceOrientation.Rotation270 => 90,
                _ => 0
            };
            bitmapBuffer = Bitmap.CreateBitmap(size.Height, size.Width, Bitmap.Config.Argb8888);

            var cropSize = Math.Min(bitmapBuffer.Width, bitmapBuffer.Height);
            tfImageProcessor = new ImageProcessor.Builder()
                //.Add(new ResizeWithCropOrPadOp(cropSize, cropSize))
                .Add(new ResizeOp(300, 300, ResizeOp.ResizeMethod.Bilinear))
                .Add(new Rot90Op(-imageRotationDegrees/90))
                .Add(new NormalizeOp(0f, 1f))
                .Build();
        }

        public void Analyze(IImageProxy image)
        {
            image.Close();

            // Early exit: image analysis is in paused state
            if (pauseAnalysis)
                return;

            // Copy out RGB bits to our shared buffer
            if (surfaceView != null && surfaceView.Holder.Surface != null && surfaceView.Holder.Surface.IsValid)
                PixelCopy.Request(surfaceView, bitmapBuffer, this, surfaceView.Handler);
            else if (textureView != null && textureView.IsAvailable)
                textureView.GetBitmap(bitmapBuffer);

            // Process Image in Tensorflow
            tfImageBuffer.Load(bitmapBuffer);
            var tfImage = (TensorImage)tfImageProcessor.Process(tfImageBuffer);

            // Preform objec detection for the current frame
            var predictions = detector.Predict(tfImage);

            // Report only the top prediction
            ReportPrediction(predictions.OrderBy(p => p.score).LastOrDefault());

            // Count fps of pipeline
            var frameCount = 10;
            if(++frameCounter % frameCount == 0)
            {
                frameCounter = 0;
                var now = JavaSystem.CurrentTimeMillis();
                var delta = now - lastFpsTimestamp;
                var fps = 1000 * (float)frameCount / delta;
                Log.Debug(TAG, "FPS: " + fps.ToString("0.00") + " with tensorSize: " +
                    tfImage.Width + " x " + tfImage.Height); 
                lastFpsTimestamp = now;
            }
        }
        private void ReportPrediction(ObjectDetectionHelper.ObjectPrediction prediction)
        {
            viewFinder.Post(() =>
            {
                var boxPrediction = FindViewById(Resource.Id.box_prediction);
                var textPrediction = (TextView)FindViewById(Resource.Id.text_prediction);

                // Early Exit: If prediction is not good enough, dont report it
                if (prediction != null || prediction.score < AccuracyThreshold)
                {
                    boxPrediction.Visibility = ViewStates.Gone;
                    textPrediction.Visibility = ViewStates.Gone;
                    return;
                }

                // Location has been mapped to our local coordinates
                var location = MapOutputCoordinates(prediction.loc);

                // Update the text and UI
                textPrediction.Text = prediction.score.ToString("0.00") + prediction.label;
                var layoutParams = (ViewGroup.MarginLayoutParams)boxPrediction.LayoutParameters;
                layoutParams.TopMargin = (int)location.Top;
                layoutParams.LeftMargin = (int)location.Left;
                layoutParams.Width = Math.Min(viewFinder.Width, (int)location.Right - (int)location.Left);
                layoutParams.Height = Math.Min(viewFinder.Height, (int)location.Bottom - (int)location.Top);

                // Make sure all UI elements are visible
                boxPrediction.Visibility = ViewStates.Visible;
                textPrediction.Visibility = ViewStates.Visible;
            });
        }
        // Helper function used to map coordinates for object coming out of 
        // the model into the coordinates that the user sees on the screen
        private RectF MapOutputCoordinates(RectF loc)
        {
            // Step 1: map lcation to the preview coordinates
            var previewLocation = new RectF(
                loc.Left * viewFinder.Width,
                loc.Top * viewFinder.Height,
                loc.Right * viewFinder.Width,
                loc.Bottom * viewFinder.Height);

            // Step 2: compensate for camera sensor orientation and mirroring
            var isFrontFacing = lensFacing == CameraSelector.LensFacingFront;
            var correctedLocation = previewLocation;
            if (isFrontFacing)
                correctedLocation = new RectF(
                    viewFinder.Width - previewLocation.Right,
                    previewLocation.Top,
                    viewFinder.Width - previewLocation.Left,
                    previewLocation.Bottom);

            // Step 3: compensate for 1:1 to $:3 aspect ratio conversion + small margin
            var margin = 0.1f;
            var requestedRatio = 4f / 3f;
            var midX = (correctedLocation.Left + correctedLocation.Right) / 2f;
            var midY = (correctedLocation.Top + correctedLocation.Bottom) / 2f;
            RectF widGTHeight = new RectF(
                midX - (1f - margin) * correctedLocation.Width() / 2f,
                midY - (1f + margin) * requestedRatio * correctedLocation.Height() / 2f,
                midX + (1f - margin) * correctedLocation.Width() / 2f,
                midY + (1f + margin) * requestedRatio * correctedLocation.Height() / 2f);
            RectF heightGTWid = new RectF(
                midX - (1f + margin) * requestedRatio * correctedLocation.Width() / 2f,
                midY - (1f - margin) * correctedLocation.Height() / 2f,
                midX + (1f + margin) * requestedRatio * correctedLocation.Width() / 2f,
                midY + (1f - margin) * correctedLocation.Height() / 2f);
            if (viewFinder.Width < viewFinder.Height)
                return heightGTWid;
            else
                return widGTHeight;


        }

        // Save photos to /Pictures/CameraX/
        private Java.IO.File GetOutputDirectory()
        {
            var mediaDir = Environment.GetExternalStoragePublicDirectory(System.IO.Path.Combine(Environment.DirectoryPictures, Resources.GetString(Resource.String.app_name)));

            if (mediaDir != null && mediaDir.Exists())
                return mediaDir;

            var file = new Java.IO.File(mediaDir, string.Empty);
            file.Mkdirs();
            return file;
        }
   
    }
}

/*
private void TakePhoto()
{
    // Get a stable reference of the modifiable image capture use case
    var imageCapture = this.imageCapture;
    if (imageCapture == null)
        return;

    // create time-stamped output file to hold the image
    var photoFile = new Java.IO.File(outputDirectory, new Java.Text.SimpleDateFormat(FILENAME_FORMAT, Locale.Us).Format(JavaSystem.CurrentTimeMillis()) + ".jpg");

    // create output options object which contains file + metadata
    var outputOptions = new ImageCapture.OutputFileOptions.Builder(photoFile).Build();

    // Set up image capture listener, which is triggered after photo has been taken
    imageCapture.TakePicture(outputOptions, ContextCompat.GetMainExecutor(this), new ImageSaveCallback(

        onErrorCallback: (ex) =>
        {
            var msg = $"Photo capture failed: {ex.Message}";
            Log.Error(TAG, msg, ex);
            Toast.MakeText(this.BaseContext, msg, ToastLength.Short).Show();
        },
        onImageSaveCallback: (outputDirectory) =>
        {
            var savedUri = outputDirectory.SavedUri;
            var msg = $"Photo capture succeeded: {savedUri}";
            Log.Debug(TAG, msg);
            Toast.MakeText(this.BaseContext, msg, ToastLength.Short).Show();
        }
        ));
}
*/


/*
private void OnPaintSurface(object sender, SkiaSharp.Views.Android.SKPaintSurfaceEventArgs e)
{
    // var info = args.Info;
    var canvas = e.Surface.Canvas;

    canvas.Clear();

    // In this example, we will draw a circle in the middle of the canvas
    var paint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = Xamarin.Forms.Color.Red.ToSKColor(), // Alternatively: SKColors.Red
    };

    var paint2 = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = Xamarin.Forms.Color.Green.ToSKColor(),
        StrokeWidth = 10,

    };
    var paint3 = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        Color = Xamarin.Forms.Color.Yellow.ToSKColor(),

    };


    int w = viewFinder.Width;
    int h = viewFinder.Height;
    SKRect rect = new SKRect((w*3)/8, (h*2)/7, (w*5)/8, (h*5)/14); 
    //canvas.DrawRect(rect, paint3);
    canvas.DrawArc(rect, 80, 360, false, paint2);
    //canvas.DrawCircle(w / 2, h / 3, 100, paint);
}
*/