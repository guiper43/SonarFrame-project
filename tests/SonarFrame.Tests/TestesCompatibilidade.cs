using System.Text.Json;
using Microsoft.Data.Sqlite;
using SonarFrame.Core;
using SonarFrame.Infrastructure;
using Xunit;

namespace SonarFrame.Tests;

public sealed class TestesCompatibilidade : IDisposable
{
    private readonly string diretorio = Path.Combine(AppContext.BaseDirectory, "compatibilidade", Guid.NewGuid().ToString("N"));

    public TestesCompatibilidade() => Directory.CreateDirectory(diretorio);

    public void Dispose() => Directory.Delete(diretorio, true);

    [Fact]
    public async Task ProjetoComNomesOriginaisAbreESalvaSemPerderDados()
    {
        const string documento = """
            {
              "Id": "10000000-0000-0000-0000-000000000001",
              "Name": "Projeto anterior",
              "Library": [{
                "Id": "20000000-0000-0000-0000-000000000001",
                "Name": "Vídeo original", "Path": "original.mp4", "Kind": 0,
                "Duration": {"Numerator": 10, "Denominator": 1},
                "FrameRate": {"Numerator": 30000, "Denominator": 1001, "FrameDuration": {"Numerator": 1001, "Denominator": 30000}},
                "VariableFrameRate": true
              }],
              "Timeline": [{
                "Id": "30000000-0000-0000-0000-000000000001",
                "AssetId": "20000000-0000-0000-0000-000000000001",
                "Name": "Trecho", "SourceStart": {"Numerator": 1, "Denominator": 1},
                "Duration": {"Numerator": 6, "Denominator": 1}, "Volume": 70
              }],
              "Layers": [{
                "Clip": {
                  "Id": "40000000-0000-0000-0000-000000000001",
                  "AssetId": "20000000-0000-0000-0000-000000000001",
                  "Name": "Camada", "SourceStart": {"Numerator": 2, "Denominator": 1},
                  "Duration": {"Numerator": 3, "Denominator": 1}, "Volume": 40
                },
                "Start": {"Numerator": 1, "Denominator": 1}
              }],
              "Cursor": {"Numerator": 2, "Denominator": 1},
              "Selection": {"Anchor": {"Numerator": 1, "Denominator": 1}, "Active": {"Numerator": 2, "Denominator": 1}},
              "SelectedItemId": "40000000-0000-0000-0000-000000000001"
            }
            """;
        var caminho = Path.Combine(diretorio, "anterior.sonarframe");
        using (var connection = new SqliteConnection($"Data Source={caminho};Pooling=False"))
        {
            connection.Open();
            using var comando = connection.CreateCommand();
            comando.CommandText = """
                PRAGMA application_id = 1397117517;
                PRAGMA user_version = 1;
                CREATE TABLE project (id INTEGER PRIMARY KEY CHECK(id = 1), document TEXT NOT NULL);
                INSERT INTO project VALUES (1, $document);
                """;
            comando.Parameters.AddWithValue("$document", documento);
            comando.ExecuteNonQuery();
        }
        var repositorio = new RepositorioProjetosSqlite();
        var projeto = await repositorio.CarregarAsync(caminho);
        Assert.Equal("Projeto anterior", projeto.Nome);
        Assert.Equal("original.mp4", projeto.Biblioteca[0].Caminho);
        Assert.Equal(new TaxaQuadros(30000, 1001), projeto.Biblioteca[0].TaxaQuadros);
        Assert.True(projeto.Biblioteca[0].TaxaQuadrosVariavel);
        Assert.Equal(TempoMidia.Segundos(6), projeto.Duracao);
        Assert.Equal(TempoMidia.Segundos(1), projeto.LinhaDoTempo[0].InicioNaOrigem);
        Assert.Equal(70, projeto.LinhaDoTempo[0].Volume);
        Assert.Equal(40, projeto.Camadas[0].Clipe.Volume);
        Assert.Equal(TempoMidia.Segundos(1), projeto.Camadas[0].Inicio);
        Assert.Equal(TempoMidia.Segundos(2), projeto.Camadas[0].Clipe.InicioNaOrigem);
        Assert.Equal(new SelecaoTemporal(TempoMidia.Segundos(1), TempoMidia.Segundos(2)), projeto.Selecao);
        Assert.Equal(projeto.Camadas[0].Clipe.Id, projeto.ItemSelecionadoId);
        Assert.Equal(TempoMidia.Segundos(2), projeto.Cursor);
        await repositorio.SalvarAsync(caminho, projeto);
        using var esperado = JsonDocument.Parse(documento);
        using var gravado = JsonDocument.Parse(JsonSerializer.Serialize(await repositorio.CarregarAsync(caminho)));
        Assert.True(JsonElement.DeepEquals(esperado.RootElement, gravado.RootElement));
    }

    [Fact]
    public void ConfiguracaoComChaveOriginalPreservaDiretorio()
    {
        var caminho = Path.Combine(diretorio, "settings.json");
        File.WriteAllText(caminho, "{\"ProjectsDirectory\":" + JsonSerializer.Serialize(diretorio) + "}");
        var repositorio = new RepositorioConfiguracoes(caminho);
        var configuracoes = repositorio.Carregar();
        Assert.Equal(diretorio, configuracoes.DiretorioProjetos);
        repositorio.Salvar(configuracoes);
        using var documento = JsonDocument.Parse(File.ReadAllText(caminho));
        Assert.Equal(diretorio, documento.RootElement.GetProperty("ProjectsDirectory").GetString());
    }
}
