using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace B1SLayer
{
    /// <summary>
    /// Represents a single request to be sent in a batch to the Service Layer.
    /// </summary>
    public class SLBatchRequest
    {
        public HttpMethod HttpMethod { get; set; }
        public string Resource { get; set; }
        public object Data { get; set; }
        public int? ContentID { get; set; }
        public Encoding Encoding { get; set; }
        public JsonSerializerSettings JsonSerializerSettings { get; set; }

        public SLBatchRequest(HttpMethod httpMethod, string resource, object data = null, int? contentID = null)
        {
            HttpMethod = httpMethod;
            Resource = resource;
            Data = data;
            ContentID = contentID;
            Encoding = Encoding.UTF8;
            JsonSerializerSettings = new JsonSerializerSettings();
        }
    }
}