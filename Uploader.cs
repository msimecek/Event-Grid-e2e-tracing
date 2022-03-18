using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using System.Collections.Generic;
using System.Diagnostics;
using Azure.Messaging.EventGrid;
using Azure;

namespace EventGridBlobTracing
{
    public class Uploader
    {
        [FunctionName("Uploader")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [Blob("uploads/{rand-guid}.png", FileAccess.ReadWrite, Connection = "TargetStorageConnectionString")] BlobClient blobClient,
            ILogger log)
        {
            string path = "";

            // simple distinction between local and Azure environment
#if !DEBUG
         //   if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")))
                path = Environment.GetEnvironmentVariable("HOME") + "\\site\\wwwroot";
            // else
#else
            path = ".";
#endif
            path += "\\Data\\picture.png";

            log.LogInformation($"*** Uploading picture...");
            await blobClient.UploadAsync(path);

            log.LogInformation($"*** Setting metadata. operationid = {Activity.Current.Id}");
            var metadata = new Dictionary<string, string>()
            {
                { "traceid", Activity.Current.Context.TraceId.ToString() },
                { "spanid", Activity.Current.Context.SpanId.ToString() },
                { "tracestate", Activity.Current.Context.TraceState }
            };

            blobClient.SetMetadata(metadata);
            
            return new OkObjectResult("Uploaded");
        }

        /// <summary>
        /// Uploads a file to Azure Blob Storage and sends event to Event Grid.
        /// </summary>
        [FunctionName("UploaderCustom")]
        public async Task<IActionResult> UploaderCustom(
           [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
           [Blob("uploads/{rand-guid}.png", FileAccess.ReadWrite, Connection = "TargetStorageConnectionString")] BlobClient blobClient,
           ILogger log)
        {
            string path = "";

#if !DEBUG
         //   if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")))
                path = Environment.GetEnvironmentVariable("HOME") + "\\site\\wwwroot";
            // else
#else
            path = ".";
#endif
            path += "\\Data\\picture.png";

            log.LogInformation($"*** Uploading picture...");
            var resp = await blobClient.UploadAsync(path);

            log.LogInformation($"*** Sending Event Grid event...");
            var eventGridClient = new EventGridPublisherClient(
                new Uri(System.Environment.GetEnvironmentVariable("EventGridEndpoint")), 
                new AzureKeyCredential(System.Environment.GetEnvironmentVariable("EventSenderGridKey")));
            
            var @event = new EventGridEvent("fileUploader", "Custom.Storage.BlobCreated", "1.0", new BlockBlobEventData()
            {
                // these properties are not required, just simulating the actual blob event
                api = "PutBlockList",
                contentType = "application/octet-stream",
                blobType = "BlockBlob",
                url = blobClient.Uri.AbsoluteUri,
                clientRequestId = Activity.Current.Context.TraceId.ToString(),
                requestId = Activity.Current.Context.SpanId.ToString()
            });
            await eventGridClient.SendEventAsync(@event);

            return new OkObjectResult("Uploaded");
        }
    }

   

}
