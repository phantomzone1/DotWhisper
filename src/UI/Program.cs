using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using DotWhisper.Core.Api;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Pipeline;
using DotWhisper.Core.Settings;

namespace DotWhisper.UI;

static class Program
{
    private const string MutexName = "DotWhisper_SingleInstance";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // TODO: Flash existing tray icon via IPC
            return;
        }

        ApplicationConfiguration.Initialize();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("config.json", optional: false, reloadOnChange: false)
            .Build();

        var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs", "dotwhisper-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLogLevel(configuration["Logging:MinimumLevel"]))
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();

            // Settings
            services.Configure<WhisperSettings>(configuration.GetSection("Whisper"));
            services.Configure<AudioSettings>(configuration.GetSection("Audio"));
            services.Configure<UiSettings>(configuration.GetSection("UI"));
            services.Configure<LoggingSettings>(configuration.GetSection("Logging"));

            // Logging
            services.AddLogging(builder => builder.AddSerilog(dispose: true));

            // Core services
            services.AddHttpClient<ITranscriptionClient, WhisperTranscriptionClient>();
            services.AddSingleton<IAudioCapture, AudioCapture>();
            services.AddSingleton<ITranscriptionPipeline, TranscriptionPipeline>();

            // Text processors (ordered pipeline)
            // Register ITextProcessor implementations here as they are added

            var provider = services.BuildServiceProvider();

            var context = new TrayApplicationContext(
                provider.GetRequiredService<ITranscriptionPipeline>(),
                provider.GetRequiredService<ITranscriptionClient>(),
                provider.GetRequiredService<IAudioCapture>(),
                provider.GetRequiredService<IOptions<WhisperSettings>>(),
                provider.GetRequiredService<IOptions<UiSettings>>(),
                provider.GetRequiredService<ILogger<TrayApplicationContext>>(),
                logFilePath);

            Application.Run(context);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static Serilog.Events.LogEventLevel ParseLogLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "verbose" => Serilog.Events.LogEventLevel.Verbose,
        "debug" => Serilog.Events.LogEventLevel.Debug,
        "warning" => Serilog.Events.LogEventLevel.Warning,
        "error" => Serilog.Events.LogEventLevel.Error,
        "fatal" => Serilog.Events.LogEventLevel.Fatal,
        _ => Serilog.Events.LogEventLevel.Information
    };
}
