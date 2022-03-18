namespace EventGridBlobTracing
{
    public record BlockBlobEventData
    {
        public string api { get; set; }
        public string contentType { get; set; }
        public int contentLength { get; set; }
        public string blobType { get; set; }
        public string url { get; set; }
        public string clientRequestId { get; set; }
        public string requestId { get; set; }
    }

}
