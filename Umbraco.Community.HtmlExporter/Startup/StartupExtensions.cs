namespace Umbraco.Community.HtmlExporter.Startup;

public static class StartupExtensions
{
    public static IServiceCollection AddHtmlExporter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExportHtmlSettings>(configuration.GetSection("ExportHtmlSettings"));
        services.AddHttpClient();
        return services;
    }
}