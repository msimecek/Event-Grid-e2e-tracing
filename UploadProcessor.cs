// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName=UploadProcessor
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid;
using System.IO;
using Azure.Storage.Blobs.Specialized;
using Azure.Messaging;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System.Net;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.Json;

namespace StorageProcessor
{
    public static class UploadProcessor
    {
        private readonly record struct AzureMonitorLink(string operation_Id, string id); // field names must be exactly this

        /// <summary>
        /// Processing function triggered by Event Grid event. Loads blob from storage and sends message to Service Bus.
        /// </summary>
        [FunctionName("UploadProcessor")]
        [return: ServiceBus("outputqueue", Connection = "ServiceBusOutputConnectionString")]
        public static string Run(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.ReadWrite, Connection = "TargetStorageConnectionString")] BlockBlobClient input,
            ILogger log)
        {
            log.LogInformation("Processing...");

            // read metadata from the blob
            var properties = input.GetProperties();
            var metadata = properties.Value.Metadata;

            // expecting to find trace ID and span ID as separate values (for easier processing)
            if (metadata.ContainsKey("traceid") && metadata.ContainsKey("spanid"))
            {
                // link this activity to the upload operation
                Activity.Current.AddTag("_MS.links", JsonSerializer.Serialize(new[] { new AzureMonitorLink(metadata["traceid"], metadata["spanid"]) }));
            }

            log.LogInformation("Done.");
            return "OK";
        }

        /// <summary>
        /// Processing function for Cloud Events. This has to be HTTP triggered, because Azure Functions don't support Event Grid trigger for Cloud Events.
        /// </summary>
        [FunctionName("UploadProcessorHttp")]
        public static async Task<HttpResponseMessage> RunHttp([HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = null)] HttpRequestMessage req, ILogger log)
        {
            // handle initial handshake
            if (req.Method == HttpMethod.Options)
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Webhook-Allowed-Origin", "eventgrid.azure.net");

                return response;
            }

            var requestmessage = await req.Content.ReadAsStringAsync();
            var cloudEvent = CloudEvent.Parse(BinaryData.FromString(requestmessage));

            // parse tracing information from the event
            if (cloudEvent.ExtensionAttributes.TryGetValue("traceparent", out var tp) && tp is string traceparent)
            {
                var traceId = traceparent.Substring(3, 32); // traceId
                var spanId = traceparent.Substring(36, 16); // spanId

                Activity.Current.AddTag("_MS.links", JsonSerializer.Serialize(new[] { new AzureMonitorLink(traceId, spanId) }));
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
