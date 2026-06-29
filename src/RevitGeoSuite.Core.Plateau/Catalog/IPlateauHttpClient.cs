using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public interface IPlateauHttpClient
{
    Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken);

    Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken);

    Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken);

    Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken);
}
