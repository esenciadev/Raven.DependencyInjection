using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.DependencyInjection;

namespace YourNamespace
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds a Raven <see cref="IDocumentStore"/> singleton to the dependency injection services.
        /// The document store is configured using a <see cref="RavenSettings"/> section in your appsettings.json file.
        /// </summary>
        /// <param name="services">The dependency injection services.</param>
        /// <returns>The dependency injection services.</returns>
        public static IServiceCollection AddRavenDbDocStore(this IServiceCollection services)
        {
            return services.AddRavenDbDocStore(options => { });
        }

        /// <summary>
        /// Adds a Raven <see cref="IDocumentStore"/> singleton to the dependency injection services.
        /// The document store is configured based on the <see cref="RavenOptions"/> action configuration.
        /// </summary>
        /// <param name="services">The dependency injection services.</param>
        /// <param name="options">The configuration for the <see cref="RavenOptions"/></param>
        /// <returns>The dependency injection services.</returns>
        public static IServiceCollection AddRavenDbDocStore(
            this IServiceCollection services,
            Action<RavenOptions> options)
        {
            services.ConfigureOptions<RavenOptionsSetup>();
            services.Configure(options);
            services.AddSingleton(sp =>
            {
                var setup = sp.GetRequiredService<IOptions<RavenOptions>>().Value;
                return setup.GetDocumentStore(setup.BeforeInitializeDocStore);
            });

            return services;
        }
    }
    
    public class RavenOptions
    {
        /// <summary>
        /// The Raven Db basic configuration information.
        /// The default configuration information is loaded via <see cref="SectionName"/> parameter.
        /// </summary>
        public RavenSettings Settings { get; set; }

        /// <summary>
        /// The name of the configuration section for <see cref="RavenSettings"/>.
        /// The default value is <see cref="RavenSettings"/>.
        /// </summary>
        public string SectionName { get; set; } = nameof(RavenSettings);

        /// <summary>
        /// Gets the <see cref="IConfiguration"/> object.
        /// The default value is set to context of the execution.
        /// </summary>
        public IConfiguration GetConfiguration { get; set; }

        /// <summary>
        /// The default value is set to <see cref="IHostingEnvironment"/>.
        /// This will change with AspNetCore 3.0 version.
        /// </summary>
        public IHostEnvironment GetHostingEnvironment { get; set; }

        /// <summary>
        /// The certificate file for the <see cref="IDocumentStore"/>.
        /// </summary>
        public X509Certificate2 Certificate { get; set; }

        /// <summary>
        /// Gets instance of the <see cref="IDocumentStore"/>.
        /// </summary>
        public Func<Action<IDocumentStore>, IDocumentStore> GetDocumentStore { get; set; }

        /// <summary>
        /// Action executed on the document store prior to calling docStore.Initialize(...).
        /// This should be used to configure RavenDB conventions.
        /// </summary>
        /// <example>
        ///     <code>
        ///         services.AddRavenDbDocStore(options =>
        ///         {
        ///             options.BeforeInitializeDocStore = docStore => docStore.Conventions.IdentityPartsSeparator = "-";
        ///         }
        ///     </code>
        /// </example>
        public Action<IDocumentStore> BeforeInitializeDocStore { get; set; }
    }

    public class RavenOptionsSetup : IConfigureOptions<RavenOptions>, IPostConfigureOptions<RavenOptions>
    {
        private readonly IHostEnvironment _hosting;
        private readonly IConfiguration _configuration;
        private RavenOptions _options;

        /// <summary>
        /// The constructor for <see cref="RavenOptionsSetup"/>.
        /// </summary>
        /// <param name="hosting"></param>
        /// <param name="configuration"></param>
        public RavenOptionsSetup(
            IHostEnvironment hosting,
            IConfiguration configuration)
        {
            _hosting = hosting;
            _configuration = configuration;
        }

        /// <summary>
        /// The default configuration if needed.
        /// </summary>
        /// <param name="options"></param>
        public void Configure(RavenOptions options)
        {
            if (options.Settings == null)
            {
                var settings = new RavenSettings();
                _configuration.Bind(options.SectionName, settings);

                options.Settings = settings;
            }

            if (options.GetHostingEnvironment == null)
            {
                options.GetHostingEnvironment = _hosting;
            }

            if (options.GetConfiguration == null)
            {
                options.GetConfiguration = _configuration;
            }
        }

        /// <summary>
        /// Post configuration for <see cref="RavenOptions"/>.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        public void PostConfigure(string name, RavenOptions options)
        {
            _options = options;

            if (options.Certificate == null)
            {
                options.Certificate = GetCertificateFromFileSystem();
                _options.Certificate = options.Certificate;
            }

            if (options.GetDocumentStore == null)
            {
                options.GetDocumentStore = GetDocumentStore;
            }
        }

        private IDocumentStore GetDocumentStore(Action<IDocumentStore> configureDbStore)
        {
            if (string.IsNullOrEmpty(_options.Settings.DatabaseName))
            {
                throw new InvalidOperationException("You haven't configured a DatabaseName. Ensure your appsettings.json contains a RavenSettings section.");
            }
            if (_options.Settings.Urls == null || _options.Settings.Urls.Length == 0)
            {
                throw new InvalidOperationException("You haven't configured your Raven database URLs. Ensure your appsettings.json contains a RavenSettings section.");
            }

            var documentStore = new DocumentStore
            {
                Urls = _options.Settings.Urls,
                Database = _options.Settings.DatabaseName
            };

            if (_options.Certificate != null)
            {
                documentStore.Certificate = _options.Certificate;
            }

            configureDbStore?.Invoke(documentStore);

            documentStore.Initialize();

            return documentStore;
        }

        private X509Certificate2 GetCertificateFromFileSystem()
        {
            var certRelativePath = _options.Settings.CertFilePath;

            if (!string.IsNullOrEmpty(certRelativePath))
            {
                var certFilePath = Path.Combine(_options.GetHostingEnvironment.ContentRootPath, certRelativePath);
                if (!File.Exists(certFilePath))
                {
                    throw new InvalidOperationException($"The Raven certificate file, {certRelativePath} is missing. Expected it at {certFilePath}.");
                }

                return new X509Certificate2(certFilePath, _options.Settings.CertPassword);
            }

            return null;
        }
    }
}
