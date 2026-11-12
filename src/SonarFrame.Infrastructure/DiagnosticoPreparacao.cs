namespace SonarFrame.Infrastructure;

public sealed class DiagnosticoPreparacao
{
    public int BlocosConsultados { get; internal set; }
    public int CacheHits { get; internal set; }
    public int CacheMisses { get; internal set; }
    public int BlocosRenderizados { get; internal set; }
    public bool PreviaReutilizada { get; internal set; }
    public bool Concluida { get; internal set; }
    public double RenderizacaoComMixagemMs { get; internal set; }
    public double? MixagemAudioMs { get; internal set; }
    public double RenderizacaoVideoMs { get; internal set; }
    public double CodificacaoAudioMs { get; internal set; }
    public int VideosRenderizados { get; internal set; }
    public int AudiosRenderizados { get; internal set; }
    public double MontagemFinalMs { get; internal set; }
    public double TotalPreparacaoMs { get; internal set; }
    public double ResolucaoComposicaoMs { get; internal set; }
    public double ConsultaCacheMs { get; internal set; }
    public double CriacaoProcessosFfmpegMs { get; internal set; }
    public int ProcessosFfmpeg { get; internal set; }
}
