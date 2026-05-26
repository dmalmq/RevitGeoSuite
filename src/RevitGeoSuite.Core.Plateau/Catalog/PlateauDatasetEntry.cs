using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauDatasetEntry
{
    [JsonProperty("format")]
    public string? Format { get; set; }

    [JsonProperty("format_version")]
    public string? FormatVersion { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("type_en")]
    public string? TypeEn { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("composite_url")]
    public string? CompositeUrl { get; set; }

    [JsonProperty("lod")]
    public string? Lod { get; set; }

    [JsonProperty("texture")]
    public bool? Texture { get; set; }

    [JsonProperty("city_code")]
    public string? CityCode { get; set; }

    [JsonProperty("ward_code")]
    public string? WardCode { get; set; }

    [JsonProperty("city")]
    public string? City { get; set; }

    [JsonProperty("ward")]
    public string? Ward { get; set; }

    [JsonProperty("pref")]
    public string? Pref { get; set; }

    [JsonProperty("year")]
    [JsonConverter(typeof(TolerantNullableInt32JsonConverter))]
    public int? Year { get; set; }

    [JsonProperty("registration_year")]
    [JsonConverter(typeof(TolerantNullableInt32JsonConverter))]
    public int? RegistrationYear { get; set; }

    [JsonProperty("interior")]
    public bool? Interior { get; set; }

    [JsonIgnore]
    public PlateauCatalogSource CatalogSource { get; set; } = PlateauCatalogSource.Dataset;

    public string? PreferredUrl => string.IsNullOrEmpty(CompositeUrl) ? Url : CompositeUrl;
}

public enum PlateauCatalogSource
{
    Dataset = 0,
    Latest = 1
}
