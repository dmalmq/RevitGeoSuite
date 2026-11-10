using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using NetTopologySuite.IO;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RevitGeoSuite.FloorPlanExport.Core.UnitFloors;

/// <summary>One feature table of a GeoPackage: its name, columns, row count and CRS.</summary>
public sealed class GisFeatureTableInfo
{
    public GisFeatureTableInfo(
        string name,
        string geometryColumn,
        string? fidColumn,
        IReadOnlyList<string> columns,
        int featureCount,
        int? epsg)
    {
        Name = name;
        GeometryColumn = geometryColumn;
        FidColumn = fidColumn;
        Columns = columns;
        FeatureCount = featureCount;
        Epsg = epsg;
    }

    public string Name { get; }

    public string GeometryColumn { get; }

    /// <summary>The integer primary key column (usually <c>fid</c>), when the table has one.</summary>
    public string? FidColumn { get; }

    /// <summary>The attribute columns, without the primary key and the geometry.</summary>
    public IReadOnlyList<string> Columns { get; }

    public int FeatureCount { get; }

    public int? Epsg { get; }
}

/// <summary>One row of a feature table: its primary key, attributes as text, and geometry.</summary>
public sealed class GisFeature
{
    public GisFeature(long fid, IReadOnlyDictionary<string, string?> attributes, NtsGeometry? geometry)
    {
        Fid = fid;
        Attributes = attributes;
        Geometry = geometry;
    }

    public long Fid { get; }

    public IReadOnlyDictionary<string, string?> Attributes { get; }

    /// <summary>The geometry in the file's CRS, or null when the row has none or it cannot be read.</summary>
    public NtsGeometry? Geometry { get; }
}

/// <summary>The rows of one feature table, still in the file's CRS.</summary>
public sealed class GisFeatureTable
{
    public GisFeatureTable(
        GisFeatureTableInfo info,
        IReadOnlyList<GisFeature> features,
        int? sourceEpsg,
        string? sourceWkt,
        IReadOnlyList<string> warnings)
    {
        Info = info;
        Features = features;
        SourceEpsg = sourceEpsg;
        SourceWkt = sourceWkt;
        Warnings = warnings;
    }

    public GisFeatureTableInfo Info { get; }

    public IReadOnlyList<GisFeature> Features { get; }

    public int? SourceEpsg { get; }

    public string? SourceWkt { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Reads GeoPackage feature tables with their attributes and row keys. <see cref="GeoPackageGeometryReader"/>
/// reads geometry only, for basemaps; unit floors need each row's key and fields so a created floor can be
/// traced back to the row it came from.
/// </summary>
public static class GeoPackageFeatureReader
{
    public static IReadOnlyList<GisFeatureTableInfo> ListTables(string geoPackagePath)
    {
        using SqliteConnection connection = Open(geoPackagePath);
        List<GisFeatureTableInfo> tables = new();
        foreach ((string tableName, string geometryColumn, int srsId) in GeoPackageGeometryReader.ReadFeatureTables(connection))
        {
            tables.Add(ReadInfo(connection, tableName, geometryColumn, srsId));
        }

        return tables;
    }

    public static GisFeatureTable Read(string geoPackagePath, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("A table name is required.", nameof(tableName));
        }

        using SqliteConnection connection = Open(geoPackagePath);
        (string Name, string GeometryColumn, int SrsId)? match = null;
        foreach ((string name, string geometryColumn, int srsId) in GeoPackageGeometryReader.ReadFeatureTables(connection))
        {
            if (string.Equals(name, tableName, StringComparison.Ordinal))
            {
                match = (name, geometryColumn, srsId);
                break;
            }
        }

        if (match is null)
        {
            throw new InvalidOperationException($"The GeoPackage has no feature table named \"{tableName}\".");
        }

        GisFeatureTableInfo info = ReadInfo(connection, match.Value.Name, match.Value.GeometryColumn, match.Value.SrsId);
        List<string> warnings = new();
        List<GisFeature> features = new();
        WKBReader wkbReader = new();

        List<string> selected = new();
        selected.Add(info.FidColumn is null ? "rowid" : GeoPackageGeometryReader.QuoteIdentifier(info.FidColumn));
        selected.Add(GeoPackageGeometryReader.QuoteIdentifier(info.GeometryColumn));
        foreach (string column in info.Columns)
        {
            selected.Add(GeoPackageGeometryReader.QuoteIdentifier(column));
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                $"SELECT {string.Join(", ", selected)} FROM {GeoPackageGeometryReader.QuoteIdentifier(info.Name)} ORDER BY 1;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long fid = reader.GetInt64(0);
                NtsGeometry? geometry = null;
                if (!reader.IsDBNull(1))
                {
                    byte[]? wkb = GeoPackageGeometryReader.TryStripGeoPackageHeader((byte[])reader.GetValue(1));
                    if (wkb is not null)
                    {
                        try
                        {
                            geometry = wkbReader.Read(wkb);
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Row {fid.ToString(CultureInfo.InvariantCulture)}: geometry could not be read ({ex.Message}).");
                        }
                    }
                }

                Dictionary<string, string?> attributes = new(StringComparer.Ordinal);
                for (int i = 0; i < info.Columns.Count; i++)
                {
                    attributes[info.Columns[i]] = ToText(reader, i + 2);
                }

                features.Add(new GisFeature(fid, attributes, geometry));
            }
        }

        (int? epsg, string? wkt) = GeoPackageGeometryReader.ReadSourceCrs(connection, match.Value.SrsId);
        return new GisFeatureTable(info, features, epsg, wkt, warnings);
    }

    private static SqliteConnection Open(string geoPackagePath)
    {
        if (string.IsNullOrWhiteSpace(geoPackagePath))
        {
            throw new ArgumentException("A GeoPackage path is required.", nameof(geoPackagePath));
        }

        if (!File.Exists(geoPackagePath))
        {
            throw new FileNotFoundException("The GeoPackage was not found.", geoPackagePath);
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = geoPackagePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        SqliteConnection connection = new(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static GisFeatureTableInfo ReadInfo(SqliteConnection connection, string tableName, string geometryColumn, int srsId)
    {
        string? fidColumn = null;
        List<string> columns = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({GeoPackageGeometryReader.QuoteIdentifier(tableName)});";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(1);
                string type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                bool primaryKey = !reader.IsDBNull(5) && reader.GetInt64(5) > 0;
                if (string.Equals(name, geometryColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (primaryKey && fidColumn is null && type.IndexOf("INT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fidColumn = name;
                    continue;
                }

                columns.Add(name);
            }
        }

        int count;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT COUNT(*) FROM {GeoPackageGeometryReader.QuoteIdentifier(tableName)};";
            count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        (int? epsg, _) = GeoPackageGeometryReader.ReadSourceCrs(connection, srsId);
        return new GisFeatureTableInfo(tableName, geometryColumn, fidColumn, columns, count, epsg);
    }

    private static string? ToText(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        object value = reader.GetValue(ordinal);
        return value switch
        {
            string text => text,
            byte[] _ => null,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
