using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class BatchTableReaderTests
{
    [Fact]
    public void ReadAttributesForBatch_returns_per_batch_values_from_inline_arrays()
    {
        JObject json = JObject.Parse("{\"gml_id\":[\"a\",\"b\",\"c\"],\"feature_type\":[\"bldg:Building\",\"bldg:Building\",\"bldg:Building\"]}");
        IReadOnlyDictionary<string, object?> attrs = BatchTableReader.ReadAttributesForBatch(json, new byte[0], batchId: 1, batchLength: 3);
        Assert.Equal("b", attrs["gml_id"]);
        Assert.Equal("bldg:Building", attrs["feature_type"]);
    }

    [Fact]
    public void ReadAttributesForBatch_returns_empty_when_index_out_of_range()
    {
        JObject json = JObject.Parse("{\"gml_id\":[\"a\"]}");
        IReadOnlyDictionary<string, object?> attrs = BatchTableReader.ReadAttributesForBatch(json, new byte[0], batchId: 5, batchLength: 1);
        Assert.False(attrs.ContainsKey("gml_id"));
    }
}
