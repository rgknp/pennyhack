
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models;

using System;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;

namespace pennyhack
{

    public static class ExtractDate
    {
        private const string subscriptionKeyComputerVision = "d122845526eb4866a5d230771878ca68";
        private const string susbcriptionKeyCustomPrediction = "f8f733a36ef746458e27db9384cec9c0";
        private const string customVisionEndpoint = "https://southcentralus.api.cognitive.microsoft.com/";
        private const string computerVisionEndpoint = "https://pennydateocr.cognitiveservices.azure.com/";
        private const string customVisionProjectId = "c7ce015a-38af-4d9f-97db-316a038943fb";
        private const string customVisionPublishedModelName = "Iteration4";
        private const int numberOfCharsInOperationId = 36;
        

        //TO DO: Change this variable to be an input to the function
        // private const string localImagePath = @"C:\Users\rahgupt\Desktop\penny\test\IMG_5399_edited-1-copy.jpg";

        [FunctionName("ExtractDate")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, TraceWriter log, ExecutionContext executionContext)
        {
            log.Info("C# HTTP trigger function processed a request.");

            log.Info(executionContext.FunctionDirectory);
            log.Info(executionContext.FunctionAppDirectory);

            // Initiate Computer Vision client object
            ComputerVisionClient computerVision = new ComputerVisionClient(
                new ApiKeyServiceClientCredentials(subscriptionKeyComputerVision),
                new System.Net.Http.DelegatingHandler[] { });

            computerVision.Endpoint = computerVisionEndpoint;

            // Initiate Custom Vision Prediction client object
            CustomVisionPredictionClient customVisionClient = new CustomVisionPredictionClient()
            {
                ApiKey = susbcriptionKeyCustomPrediction,
                Endpoint = customVisionEndpoint
            };

            log.Info("Images being analyzed ...");

            //string dirPath = Directory.GetCurrentDirectory();
            string dirPath = executionContext.FunctionAppDirectory;

            string localImagePath = Path.Combine(executionContext.FunctionAppDirectory, "IMG_5399_edited-1.JPG");

            log.Info(localImagePath);

            //localImagePath = req.Body;
            //TO DO: Change this method to accept Stream instead of string for image and add a return JSON output
            List<string> resultList = await PredictCustomImage(customVisionClient, localImagePath, customVisionProjectId, customVisionPublishedModelName, computerVision, log, dirPath);

            foreach (string i in resultList)
            {
                log.Info("PRINTING FINAL OUTPUT" + i);
            }

            //TO DO: Change below sample functions code to return JSON output from  PredictCustomerImage
            string name = req.Query["name"];

            string requestBody = new StreamReader(req.Body).ReadToEnd();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            //return name != null
            //    ? (ActionResult)new OkObjectResult($"Hello, {name}")
            //    : new BadRequestObjectResult("Please pass a name on the query string or in the request body");

            return name == null
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("Please pass a name in the query or in the request body")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(resultList, Formatting.Indented), Encoding.UTF8, "application/json")
                };

        }

        private static async Task<List<string>> PredictCustomImage(CustomVisionPredictionClient customVisionClient, string imagePath, string projectId, string publishedModelName, ComputerVisionClient computerVision, TraceWriter log, string dirPath)
        {
            log.Info("Inside PredictCustomImage");

            List<string> getPredictionsList = new List<string>();
            string getPredictions = "Making a prediction";

            if (!File.Exists(imagePath))
            {
                string imageError = "\nUnable to open or read localImagePath:\n{0} \n" + imagePath;
                log.Info(imageError);
                getPredictionsList.Add(imageError);
                return getPredictionsList;
            }

            // Make a prediction against the new project
            log.Info("Making a prediction:");

            using (var stream = File.OpenRead(imagePath))
            {
                //   customVisionClient.DetectImageAsync
                var result = customVisionClient.DetectImage(Guid.Parse(projectId), publishedModelName, stream);

                var i = 0;
                // Loop over each prediction and write out the results
                foreach (var c in result.Predictions)
                {

                    if (c.Probability >= 0.2 && c.TagName.Equals("DATE"))
                    {
                        log.Info($"\t{c.TagName}: {c.Probability:P1} [ {c.BoundingBox.Left}, {c.BoundingBox.Top}, {c.BoundingBox.Width}, {c.BoundingBox.Height} ]");

                        Image<Rgba32> croppedDate = CropDate(c, imagePath, log);
                        var croppedFileName = c.TagName + i.ToString() + ".jpg";

                        log.Info("Saving cropped image locally");
                        croppedDate.Save(Path.Combine(dirPath, croppedFileName), new JpegEncoder { Quality = 100 });

                        log.Info(Path.Combine(dirPath, croppedFileName));

                        getPredictions = await ExtractLocalTextAsync(computerVision, Path.Combine(dirPath, croppedFileName), log);

                        getPredictionsList.Add(getPredictions);
                        //Task.WhenAll(t3).Wait(15000);

                        i++;
                    }
                }
            }

            return getPredictionsList;
        }

        // Recognize text from a local image
        private static async Task<string> ExtractLocalTextAsync(
            ComputerVisionClient computerVision, string imagePath, TraceWriter log)
        {
            string getExtractedText = "Calling computer vision to extract date text";

            if (!File.Exists(imagePath))
            {
                string imageError = "\nUnable to open or read localImagePath:\n{0} \n" + imagePath;
                log.Info(imageError);
                return imageError;

            }

            using (Stream imageStream = File.OpenRead(imagePath))
            {

                OcrResult ocrResults =
                    await computerVision.RecognizePrintedTextInStreamAsync(
                        true, imageStream);

                //OcrResult ocrResults =
                //    await computerVision.RecognizePrintedTextInStreamAsync(
                //        true, imageStream, OcrLanguages.En);

                log.Info(ocrResults.Language);
                log.Info(ocrResults.Orientation);
                // log.Info(value: ocrResults.Regions);
                //log.Info(ocrResults.TextAngle);
            }

            using (Stream imageStream = File.OpenRead(imagePath))
            {

                // Start the async process to recognize the text
                BatchReadFileInStreamHeaders textHeaders =
                    await computerVision.BatchReadFileInStreamAsync(
                        imageStream);

                getExtractedText = await GetTextAsync(computerVision, textHeaders.OperationLocation, log);

            }
            return getExtractedText;
        }

        // Retrieve the recognized text
        private static async Task<string> GetTextAsync(
            ComputerVisionClient computerVision, string operationLocation, TraceWriter log)
        {
            // Retrieve the URI where the recognized text will be
            // stored from the Operation-Location header
            string operationId = operationLocation.Substring(
                operationLocation.Length - numberOfCharsInOperationId);

            log.Info("\nCalling GetReadOperationResultAsync()");
            ReadOperationResult result =
                await computerVision.GetReadOperationResultAsync(operationId);

            // Wait for the operation to complete
            int i = 0;
            int maxRetries = 10;
            while ((result.Status == TextOperationStatusCodes.Running ||
                    result.Status == TextOperationStatusCodes.NotStarted) && i++ < maxRetries)
            {
                log.Info(
                    "Server status: {0}, waiting {1} seconds..." + result.Status + i);
                await Task.Delay(1000);

                result = await computerVision.GetReadOperationResultAsync(operationId);
            }

            // Display the results
            var recResults = result.RecognitionResults;

            log.Info("PRINT THE WHOLE LIST: JSON form of TextRecognitionResult object: ");
            log.Info(WriteListToJson(recResults, log));

            //foreach (TextRecognitionResult recResult in recResults)
            //{

            //    log.Info("JSON form of TextRecognitionResult object: ");
            //    log.Info(WriteToJson(recResult, log));

            //}

            return WriteListToJson(recResults, log);
        }

        private static String WriteToJson(TextRecognitionResult textRecognitionResult, TraceWriter log)
        {
            var stream1 = new MemoryStream();
            var ser = new DataContractJsonSerializer(typeof(TextRecognitionResult));
            ser.WriteObject(stream1, textRecognitionResult);

            stream1.Position = 0;
            var sr = new StreamReader(stream1);

            return sr.ReadToEnd();
        }

        private static String WriteListToJson(System.Collections.Generic.IList<TextRecognitionResult> recognitionResults, TraceWriter log)
        {
            var stream1 = new MemoryStream();
            var ser = new DataContractJsonSerializer(typeof(System.Collections.Generic.IList<TextRecognitionResult>));
            ser.WriteObject(stream1, recognitionResults);

            stream1.Position = 0;
            var sr = new StreamReader(stream1);

            return sr.ReadToEnd();
        }

        private static Image<Rgba32> CropDate(PredictionModel predictionModel, string imagePath, TraceWriter log)
        {
            log.Info("Cropping date from Image");

            using (Image<Rgba32> img = SixLabors.ImageSharp.Image.Load(imagePath))
            {

                Image<Rgba32> croppedDate = img.Clone(
                       i => i.Crop(new Rectangle(Convert.ToInt32(predictionModel.BoundingBox.Left * img.Width) - 5,
                Convert.ToInt32(predictionModel.BoundingBox.Top * img.Height) - 5,
                Convert.ToInt32(predictionModel.BoundingBox.Width * img.Width) + 10,
                Convert.ToInt32(predictionModel.BoundingBox.Height * img.Height) + 10))
                );

                log.Info("Date cropped from image successfully");

                return croppedDate;
            }

        }

    }

}
