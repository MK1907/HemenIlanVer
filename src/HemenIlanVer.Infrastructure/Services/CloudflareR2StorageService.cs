using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class CloudflareR2StorageService : IStorageService, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly CloudflareR2Options _opts;

    public CloudflareR2StorageService(IOptions<CloudflareR2Options> opts)
    {
        _opts = opts.Value;

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{_opts.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(_opts.AccessKeyId, _opts.SecretAccessKey),
            config);
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";

        var request = new PutObjectRequest
        {
            BucketName = _opts.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            DisablePayloadSigning = true
        };

        await _s3.PutObjectAsync(request, ct);

        return $"{_opts.PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    public void Dispose() => _s3.Dispose();
}
