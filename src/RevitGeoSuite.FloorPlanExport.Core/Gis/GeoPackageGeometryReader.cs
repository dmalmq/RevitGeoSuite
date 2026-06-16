using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using NetTopologySuite.IO;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RevitGeoSuite.FloorPlanExport.Core.Gis;

/// <summary>
/// Reads geometry from an OGC GeoPackage (<c>.gpkg</c>) SQLite database. Each feature table in
/// <c>gpkg_contents</c> becomes a layer; geometry BLOBs are decoded by stripping the GeoPackage
/// binary header (see <see cref="GeoPackage.WkbEncoder.WrapInGeoPackageHeader"/>) and parsing the
/// trailing standard WKB. The read counterpart of <see cref="GeoPackage.GpkgWriter"/>.
/// </summary>
public sealed class GeoPackageGeometryReader
{
    public GisDataset Read(string geoPackagePath)
    {
        if (string.IsNullOrWhiteSpace(geoPackagePath))
        {
            throw new ArgumentException("A GeoPackage path is required.", nameof(geoPackagePath));
        }

        if (!File.Exists(geoPackagePath))
        {
            throw new FileNotFoundException("The GeoPackage was not found.", geoPackagePath);
        }

        List<string> warnings = new();
        List<GisLayerGeometry> layers = new();
        WKBReader wkbReader = new();
        int? datasetSrsId = null;

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = geoPackagePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        using SqliteConnection connection = new(builder.ConnectionString);
        connection.Open();

        foreach ((string tableName, string geometryColumn, int srsId) in ReadFeatureTables(connection))
        {
            datasetSrsId ??= srsId;
            if (datasetSrsId != srsId)
            {
                warnings.Add(
                    $"Layer \"{tableName}\" uses a different CRS (srs {srsId}) than the rest of the file; " +
                    "it was reprojected with the first layer's CRS, which may be inaccurate.");
            }

            List<NtsGeometry> geometries = ReadLayerGeometries(connection, tableName, geometryColumn, wkbReader, warnings);
            layers.Add(new GisLayerGeometry(tableName, geometries));
        }

        if (layers.Count == 0)
        {
            warnings.Add("The GeoPackage did not contain any feature tables to import.");
        }

        (int? sourceEpsg, string? sourceWkt) = datasetSrsId.HasValue
            ? ReadSourceCrs(connection, datasetSrsId.Value)
            : (null, null);

        return new GisDataset(layers, sourceEpsg, sourceWkt, warnings);
    }

    private static IEnumerable<(string TableName, string GeometryColumn, int SrsId)> ReadFeatureTables(SqliteConnection connection)
    {
        List<(string, string, int)> tables = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            @"SELECT gc.column_name, gc.table_name, gc.srs_id
              FROM gpkg_geometry_columns gc
              JOIN gpkg_contents c ON c.table_name = gc.table_name
              WHERE c.data_type = 'features'
              ORDER BY gc.table_name;";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string geometryColumn = reader.GetString(0);
            string tableName = reader.GetString(1);
            int srsId = reader.GetInt32(2);
            tables.Add((tableName, geometryColumn, srsId));
        }

        return tables;
    }

    private static List<NtsGeometry> ReadLayerGeometries(
        SqliteConnection connection,
        string tableName,
        string geometryColumn,
        WKBReader wkbReader,
        ICollection<string> warnings)
    {
        List<NtsGeometry> geometries = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {QuoteIdentifier(geometryColumn)} FROM {QuoteIdentifier(tableName)};";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            byte[] blob = (byte[])reader.GetValue(0);
            byte[]? wkb = TryStripGeoPackageHeader(blob);
            if (wkb is null)
            {
                continue;
            }

            try
            {
                NtsGeometry geometry = wkbReader.Read(wkb);
                if (geometry is not null && !geometry.IsEmpty)
                {
                    geometries.Add(geometry);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped a geometry in \"{tableName}\": {ex.Message}");
            }
        }

        return geometries;
    }

    /// <summary>
    /// Strips the GeoPackage binary header and returns the trailing standard WKB. Header layout:
    /// magic "GP" (2) + version (1) + flags (1) + srs_id (4) + envelope (0/32/48/64 by flag bits 1-3).
    /// Returns null when the blob is not a recognizable GeoPackage geometry.
    /// </summary>
    private static byte[]? TryStripGeoPackageHeader(byte[] blob)
    {
        if (blob is null || blob.Length < 8 || blob[0] != (byte)'G' || blob[1] != (byte)'P')
        {
            return null;
        }

        byte flags = blob[3];
        int envelopeIndicator = (flags >> 1) & 0x07;
        int envelopeBytes = envelopeIndicator switch
        {
            0 => 0,
            1 => 32, // XY
            2 => 48, // XYZ
            3 => 48, // XYM
            4 => 64, // XYZM
            _ => -1, // reserved / invalid
        };

        if (envelopeBytes < 0)
        {
            return null;
        }

        int headerLength = 8 + envelopeBytes;
        if (blob.Length <= headerLength)
        {
            return null;
        }

        byte[] wkb = new byte[blob.Length - headerLength];
        Array.Copy(blob, headerLength, wkb, 0, wkb.Length);
        return wkb;
    }

    private static (int? Epsg, string? Wkt) ReadSourceCrs(SqliteConnection connection, int srsId)
    {
        if (srsId == -1 || srsId == 0)
        {
            // Undefined cartesian/geographic SRS per the GeoPackage spec — no usable CRS.
            return (null, null);
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            @"SELECT organization, organization_coordsys_id, definition
              FROM gpkg_spatial_ref_sys WHERE srs_id = $srs;";
        command.Parameters.AddWithValue("$srs", srsId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }

        string organization = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        int organizationCoordsysId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        string? definition = reader.IsDBNull(2) ? null : reader.GetString(2);

        int? epsg = string.Equals(organization, "EPSG", StringComparison.OrdinalIgnoreCase) && organizationCoordsysId > 0
            ? organizationCoordsysId
            : srsId; // srs_id is conventionally the EPSG code in GeoPackages written by this addin.

        string? wkt = string.IsNullOrWhiteSpace(definition) || string.Equals(definition, "undefined", StringComparison.OrdinalIgnoreCase)
            ? null
            : definition;

        return (epsg, wkt);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
