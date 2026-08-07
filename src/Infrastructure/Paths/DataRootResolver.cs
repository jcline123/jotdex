using Jotdex.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jotdex.Infrastructure.Paths;

public sealed class DataRootResolver : IDataRootResolver
{
    private readonly JotdexOptions _options;
    private readonly IHostEnvironment _env;

    public DataRootResolver(IOptions<JotdexOptions> options, IHostEnvironment env)
    {
        _options = options.Value;
        _env = env;
    }

    public bool IsVaultConfigured =>
        !string.IsNullOrWhiteSpace(_options.VaultPath) && Directory.Exists(_options.VaultPath);

    public string? ResolveVaultPathOrNull() =>
        IsVaultConfigured ? Path.GetFullPath(_options.VaultPath) : null;

    public string ResolveDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(_options.DataRoot))
        {
            var configured = Path.GetFullPath(_options.DataRoot);
            Directory.CreateDirectory(configured);
            return configured;
        }

        if (_options.PortableMode)
        {
            var portable = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "data"));
            Directory.CreateDirectory(portable);
            return portable;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var serviceRoot = Path.Combine(localAppData, "Jotdex");
        Directory.CreateDirectory(serviceRoot);
        return serviceRoot;
    }
}
