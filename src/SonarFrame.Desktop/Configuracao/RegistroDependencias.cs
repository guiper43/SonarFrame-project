using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SonarFrame.Application;
using SonarFrame.Infrastructure;

namespace SonarFrame.Desktop;

public static class RegistroDependencias
{
    public static IServiceCollection AdicionarServicosSonarFrame(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRepositorioProjetos, RepositorioProjetosSqlite>();
        services.AddSingleton<IExecutorFfprobe>(_ =>
        {
            var executavel = Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe");
            return new ExecutorFfprobe(File.Exists(executavel) ? executavel : "ffprobe");
        });
        services.AddSingleton<ILeitorMetadadosMidia, LeitorMetadadosFfprobe>();
        services.AddSingleton(_ => new RepositorioConfiguracoes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sonarFrame", "settings.json")));
        services.AddTransient<CatalogoProjetos>();
        services.AddScoped<IFabricaReprodutorMidia, FabricaPreviaVlc>();
        services.AddScoped<PreviaBiblioteca>();
        services.AddScoped<IPreparadorComposicao>(provedor =>
        {
            var executavel = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
            return new PreparadorComposicaoFfmpeg(
                File.Exists(executavel) ? executavel : "ffmpeg",
                provedor.GetRequiredService<IExecutorFfprobe>());
        });
        services.AddScoped<IExportador>(provedor =>
        {
            var executavel = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
            return new ExportadorFfmpeg(
                File.Exists(executavel) ? executavel : "ffmpeg",
                provedor.GetRequiredService<IExecutorFfprobe>());
        });
        services.AddTransient<JanelaPrincipal>();
        return services;
    }

    public static IHost CriarHostSonarFrame(
        string[]? argumentos = null,
        Action<IServiceCollection>? configurarServicos = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = argumentos,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AdicionarServicosSonarFrame();
        configurarServicos?.Invoke(builder.Services);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        return builder.Build();
    }
}
