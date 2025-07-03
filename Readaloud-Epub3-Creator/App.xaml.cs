using System.Configuration;
using System.Data;
using System.Windows;
    using Microsoft.Extensions.DependencyInjection;
using ModernWpf;

namespace Readaloud_Epub3_Creator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>

    public partial class App : Application
    {

        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
           
            var services = new ServiceCollection();

            var settings = JsonSettingsProvider.LoadFromFile();

            services.Configure<AppSettings>(opts =>
            {
                opts.EbooksPath = settings.EbooksPath;
                opts.Device = settings.Device;
                opts.MaxConcurrentTranscriptions = settings.MaxConcurrentTranscriptions;
            });

            services.AddSingleton<JsonSettingsProvider>();

            Services = services.BuildServiceProvider();

            base.OnStartup(e);
        }
    }


}
