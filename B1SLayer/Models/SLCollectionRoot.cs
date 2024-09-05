using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace B1SLayer.Models;

/// <summary>
/// Represents the structure of a Service Layer collection.
/// </summary>
/// <typeparam name="T"></typeparam>
public class SLCollectionRoot<T>
{
    private readonly Regex _skipRegex = new Regex(@"skip=(\d+)&?");

    /// <summary>
    /// Gets or sets the list that represents the JSON array containing the entities of the collection.
    /// </summary>
    [JsonPropertyName("value")]
    public IList<T> Value { get; set; }

    /// <summary>
    /// Gets or sets the string that represents the link to the next page.
    /// </summary>
    [JsonPropertyName("odata.nextLink")]
    public string ODataNextLink { get; set; }

    [JsonIgnore]
    private string ODataNextLinkAlt
    {
        set { ODataNextLink = value; }
    }

    /// <summary>
    /// Gets or sets the string that represents the link to the next page.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    public string ODataNextLinkJson
    {
        get => ODataNextLink;
        set => ODataNextLinkAlt = value;
    }

    /// <summary>
    /// Gets the skip number to obtain the entities of the next page.
    /// </summary>
    public int NextSkip => string.IsNullOrEmpty(ODataNextLink) ? 0 : int.Parse(_skipRegex.Match(ODataNextLink).Groups[1].Value);
}
