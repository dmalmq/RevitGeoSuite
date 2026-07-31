using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class ExportValidatorTests
{
    [Fact]
    public void Generated_citygml_profile_passes_structural_validation()
    {
        CityGmlExportPackage package = CityGmlWriterTests.CreatePackage();
        CityGmlWriter writer = new CityGmlWriter();
        ExportValidator validator = new ExportValidator();

        CityGmlValidationReport report = validator.Validate(writer.BuildXml(package), package);

        Assert.False(report.HasErrors);
    }
}
