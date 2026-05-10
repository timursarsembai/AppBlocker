using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppBlocker.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "AppBlockerSvc";
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<BlockerWorker>();
                });

            var host = builder.Build();
            host.Run();
        }
    }
}
