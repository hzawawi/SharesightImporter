﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SharesightImporter.Configuration;
using SharesightImporter.Exporter;
using SharesightImporter.Exporter.SharesiesExporter;
using SharesightImporter.SharesightClient;

namespace SharesightImporter
{
    class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var configuration = new Configuration.Configuration();
            var config = new ConfigurationBuilder().AddYamlFile("config.yml").Build();
            config.Bind(configuration);
            Validator.ValidateObject(configuration, new ValidationContext(configuration), true);

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "{Level:u4} {Timestamp:HH:mm:ss} -- {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .CreateLogger();
            return Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureLogging(
                    loggingBuilder =>
                    {
                        loggingBuilder.ClearProviders();
                        loggingBuilder.AddSerilog(dispose: true);
                    }
                )
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton(configuration);
                    services.AddHttpClient();
                    foreach (Type type in System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                        .Where(mytype => mytype.GetInterfaces().Contains(typeof(IExporterClient))))
                    {
                        var name = type.Name.Replace("ExporterClient", "");
                        var isDefined =
                            Enum.IsDefined(typeof(ExporterType), name);
                        if (!isDefined)
                        {
                            continue;
                        }
                        var anyExporter = configuration.Exporters.Any(s =>
                             s.ExporterType.ToString() == name);
                        if (!anyExporter)
                        {
                            continue;
                        }

                        var secConf = config.GetSection("Exporters").GetChildren()
                            .FirstOrDefault(s => s.GetValue<string>(nameof(ExporterType)) == name);
                        switch (name)
                        {
                            case nameof(ExporterType.Ethereum):
                                var ethConf = new EthereumExporterConfiguration();
                                secConf.Bind(ethConf);
                                Validator.ValidateObject(ethConf, new ValidationContext(ethConf), true);
                                services.AddSingleton(ethConf);
                                break;
                            case nameof(ExporterType.Sharesies):
                                var sharesiesConf = new SharesiesExporterConfiguration();
                                secConf.Bind(sharesiesConf);
                                Validator.ValidateObject(sharesiesConf, new ValidationContext(sharesiesConf), true);
                                services.AddSingleton(sharesiesConf);
                                break;
                        }

                        services.Add(new ServiceDescriptor(typeof(IExporterClient), type, ServiceLifetime.Transient));
                    }
                    services.AddTransient<ISharesightClient, SharesightClient.SharesightClient>();
                    services.AddHostedService<Worker>();
                });
        }
    }
}
