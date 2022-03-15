using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace B1SLayer.Models
{
    public class SLCollectionRoot<T>
    {
        private readonly Regex _skipRegex = new Regex(@"skip=(\d+)&?");

        [JsonProperty("value")]
        public IList<T> Value { get; set; }

        [JsonProperty("odata.nextLink")]
        public string ODataNextLink { get; set; }

        [JsonProperty("@odata.nextLink")]
        private string ODataNextLinkAlt { set { ODataNextLink = value; } }

        public int NextSkip => string.IsNullOrEmpty(ODataNextLink) ? 0 : int.Parse(_skipRegex.Match(ODataNextLink).Groups[1].Value);
    }
}
