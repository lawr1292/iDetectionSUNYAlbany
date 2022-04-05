using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AndroidX.Camera.Core;
using static AndroidX.Camera.Core.ImageCapture;

namespace CamApp2
{
    internal class ImageSaveCallback : Java.Lang.Object, IOnImageSavedCallback
    {
        private const string TAG = "EyeFinder5000";

        private readonly Action<ImageCaptureException> onErrorCallback;
        private readonly Action<OutputFileResults> onImageSaveCallback;

        public ImageSaveCallback(Action<OutputFileResults> onImageSaveCallback, Action<ImageCaptureException> onErrorCallback)
        {
            this.onErrorCallback = onErrorCallback;
            this.onImageSaveCallback = onImageSaveCallback;
        }
        public void OnError(ImageCaptureException ex)
        {
            this.onErrorCallback.Invoke(ex);
        }

        public void OnImageSaved(OutputFileResults outputFR)
        {
            this.onImageSaveCallback.Invoke(outputFR);
        }
    }
}