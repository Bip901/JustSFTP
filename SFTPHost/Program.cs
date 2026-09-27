using System.Reflection;
using JustSFTP.Protocol.Models;
using JustSFTP.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;

namespace JustSFTP.Host;

public class Program
{
    private static ILogger<Program>? _logger;

    public static async Task Main(string[] args)
    {
        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        var configuration = configurationBuilder.Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(c => c.ClearProviders().AddNLog());
        serviceCollection.Configure<SFTPServerOptions>(options => configuration.GetSection("Server").Bind(options));
        var serviceProvider = serviceCollection.BuildServiceProvider();

        _logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
            Environment.Exit(1);
        };

        IOptions<SFTPServerOptions> options = serviceProvider.GetRequiredService<IOptions<SFTPServerOptions>>();

        _logger.LogInformation("Starting server...");
        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();
        SFTPServerOptions sftpServerOptions = options.Value;
        using SFTPServer server = new(stdin, stdout, new DefaultSFTPHandler(new SFTPPath(sftpServerOptions.Root)));

        using CancellationTokenSource cts = new();
        await server.Run(cts.Token).ConfigureAwait(false);
        _logger.LogInformation("Server stopped...");
    }
}
