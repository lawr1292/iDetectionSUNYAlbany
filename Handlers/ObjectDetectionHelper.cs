using System.Collections.Generic;
using Android.Graphics;
using Java.Lang;
using Xamarin.TensorFlow.Lite;
using Org.Tensorflow.Lite.Support.Image;

namespace CamApp2
{
    public class ObjectDetectionHelper
    {
        // Abstraction object that wraps a prediction output in an easy to parse way
        public class ObjectPrediction
        {
            public RectF loc;
            public string label;
            public float score;
        }
        private const int objectCount = 1;

        private float[][][] locations = new float[1][][] { new float[objectCount][] };
        private float[][] labelIndicies = new float[1][] { new float[objectCount] };
        private float[][]scores = new float[1][] {new float[objectCount] };

        private Object locs, labInd, marks;
        private IDictionary<Integer, Object> outputBuffer;

        private Interpreter tflite;
        private IList<string> labels;

        public ObjectDetectionHelper(Interpreter tflite, IList<string> labels)
        {
            this.tflite = tflite;
            this.labels = labels;

            for (int i = 0; i < objectCount; i++)
                locations[0][i] = new float[4];
            locs = Object.FromArray(locations);
            labInd = Object.FromArray(labelIndicies);
            marks = Object.FromArray(scores);


            outputBuffer = new Dictionary<Integer, Object>()
            {
                // [new Integer(0)] = locs,
                [new Integer(0)] = labInd,
                // [new Integer(2)] = marks,tyryt
                // [new Integer(3)] = new float[1],
            };
        }

        private ObjectPrediction[] Predictions()
        {
            var objectPreds = new ObjectPrediction[objectCount];
            for(int i = 0; i < objectCount; i++)
            {
                objectPreds[i] = new ObjectPrediction
                {
                    // the locations are an array of [0,1] floats for [top, left, bottom, right]
                    loc = new RectF(
                        locations[0][i][1], locations[0][i][0],
                        locations[0][i][3], locations[0][i][2]),
                    label = labels[(int)labelIndicies[0][i]],

                    // score is a single value of [0, 1]
                    score = scores[0][i]
                };
            }

            return objectPreds;
        }

        public ObjectPrediction[] Predict(TensorImage image)
        {
            tflite.RunForMultipleInputsOutputs(new Object[] { image.Buffer }, outputBuffer);

            locations = locs.ToArray<float[][]>();
            labelIndicies = labInd.ToArray<float[]>();
            scores = marks.ToArray<float[]>();

            return Predictions();
        }
    }
}