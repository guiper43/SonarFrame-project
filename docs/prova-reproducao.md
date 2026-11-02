# Reprodução: laboratório do incremento 0

## Estado e decisão

Prova isolada em `samples/SonarFrame.ProvaReproducao`. Não altera o editor, o domínio ou o SQLite. Usa LibVLCSharp.WPF 3.10.2 e VideoLAN.LibVLC.Windows 3.0.24, com versões resolvidas e hashes em `packages.lock.json`. O build normal compila o laboratório, mas não executa testes dependentes de dispositivos multimídia.

Decisão após a prova: adotar LibVLCSharp/WPF para o incremento 1 de prévia da biblioteca, com inicialização em segundo plano; FFmpeg permanece proposto para preparação da composição no incremento 2. As verificações automatizadas passaram e o usuário aprovou o teste com leitor de telas e confirmou que conseguiu ouvir o áudio em 01/11/2026. Validação em máquina limpa e novo percurso manual na interface integrada permanecem sem confirmação. Não considerar a biblioteca aprovada para precisão quadro a quadro.

Os controles ficam fora do VideoView, evitando sobrepor botões ao HWND nativo. Imagens estáticas usam WPF Image, não um player temporal. A configuração da prova desabilita decodificação acelerada; isso não desabilita a apresentação Direct3D. Aceleração de hardware fica pendente de avaliação separada.

Após o teste manual, o usuário identificou controles temporais ativos na prévia de imagem. Corrigido: abrir uma imagem desabilita Reproduzir/Pausar, Parar, destino da busca, Buscar e Consultar posição; Abrir e seleção de mídia permanecem disponíveis. Alt+4 leva a Abrir nesse contexto. Abrir áudio/vídeo reabilita os controles temporais. A verificação automatizada cobre a troca vídeo → imagem → vídeo. O usuário retestou e aprovou a correção em 01/11/2026. Essa regra vale para a imagem isolada; uma foto inserida na composição continua tendo duração e reprodução temporal.

## Executar

Na raiz do repositório:

```powershell
./prova-reproducao.ps1
./prova-reproducao.ps1 -Benchmark
./prova-reproducao.ps1 -Manual
./prova-reproducao.ps1 -Publish
```

O primeiro comando gera amostras, verifica a composição e executa o motor em janela fora da tela, com volume zero. Retorna erro se alguma verificação falhar. `-Benchmark` acrescenta medições de preparação para durações de 1, 10 e 30 minutos. `-Manual` abre o laboratório, com áudio, e permite abrir MP4/MOV, WAV, foto e composição. `-Publish` gera uma pasta Windows x64 com runtime .NET, LibVLC, FFmpeg e FFprobe locais; não publica nem instala nada externamente.

Evidências ficam em `.tools/test-artifacts/prova-reproducao`: `resultado.json`, `benchmark.json`, mídias e capturas de busca. `falha.txt` registra falha de inicialização. Os arquivos são descartáveis e não entram no Git. Uma nova execução sobrescreve as amostras; não executar duas provas simultâneas na mesma pasta. O executável aceita `--pasta`, `--ffmpeg` e `--ffprobe` para isolar execuções.

Para repetir a prova portátil, executar seu `.exe` com `--automatico --pasta <diretório de resultados>`, ou sem `--automatico` para avaliação manual. Na pasta portátil as ferramentas são encontradas junto ao executável, independentemente do PATH. A pasta inteira deve acompanhar o `.exe`.

## O que foi verificado

- Origem MP4 de oito segundos e sua remuxagem MOV, 640×360/30 fps; WAV de segunda fonte e PNG estático.
- Corte de 1,3 a 5,3 segundos, confirmado fora de keyframe, seguido de foto com dois segundos de silêncio. Duração composta de seis segundos.
- Mudança visual vermelho/azul no quadro 81 (2,7 segundos da composição), coincidente com pulso de 1000 Hz. Tolerância do detector sonoro: 20 ms; não é uma certificação do sincronismo de saída do dispositivo físico.
- Presença simultânea de 440 Hz da origem e 880 Hz da camada, com amplitudes aproximadas 0,0795 e 0,0632; `amix normalize=0` preserva a proporção de volumes. Foto começa no quadro 120 e não contém áudio residual.
- LibVLC: reprodução, pausa com relógio estável, busca pausada para três regiões visuais, término, evento de mídia ausente e três ciclos de criação/parada/descarte. Arquivos reabertos com acesso exclusivo após parada.
- Cancelamento de FFmpeg encerra e aguarda o processo filho. Verificação com entrada infinita em tempo real, cancelada após 500 ms.
- Foto decodificada por WPF; árvore UI Automation expõe nomes dos controles e campo de destino. Inspeção da árvore não equivale a teste com NVDA.

## Medições locais em 01/11/2026

Windows 10.0.26300, processo x64, oito processadores lógicos; identificador Intel64 Family 6 Model 140 Stepping 1. A consulta de memória/modelo via CIM não estava disponível. Medições de uma execução, não percentis nem garantias. O código registra novos valores em cada execução.

| Operação | Resultado observado |
|---|---|
| Inicialização nativa | 1.437 ms |
| Preparação da composição de seis segundos | 480 ms; 47.977 bytes |
| Comando até estado Playing, com motor inicializado | 43–96 ms |
| Busca até captura com cor esperada | 25–74 ms |
| Cancelamento FFmpeg, incluindo espera programada de 500 ms | 511 ms |
| Preparação sintética de 1 minuto | 1,69 s; 468.967 bytes |
| Preparação sintética de 10 minutos | 17,86 s; 4.678.076 bytes |
| Preparação sintética de 30 minutos | 50,37 s; 13.998.421 bytes |

O relógio da API assume o destino praticamente imediatamente; por isso sua leitura não é usada como prova de quadro exibido. A captura confirma a região de cor, não identifica um quadro individual. O tempo até Playing também não mede a primeira amostra audível.

A pasta portátil passou novamente em processo próprio com PATH restrito a Windows/System32 e DOTNET_ROOT apontando para diretório inexistente; FFmpeg/FFprobe vieram da própria pasta. Isso verifica independência do PATH/runtime externo nesta máquina, não substitui máquina limpa. O artefato ocupa aproximadamente 447 MiB descompactado (runtime .NET, LibVLC x64 e ferramentas incluídos), sem as amostras geradas.

Na primeira execução dessa pasta portátil, inicializar LibVLC levou **27,3 segundos**; as operações posteriores passaram (Playing em 42–122 ms e captura da região buscada em 12–54 ms). A causa da diferença de inicialização ainda não foi isolada; não atribuí-la automaticamente a cache, antivírus ou GPU. Essa latência impede aprovar inicialização síncrona na UI de produção. O incremento 1 deverá inicializar em segundo plano, mostrar estado acessível e medir partidas frias em outras máquinas antes de prometer tempo de abertura.

Validação de regressão: `./build.ps1` compilou a solução completa sem avisos/erros e aprovou os 57 testes existentes, sem ignorados. As verificações nativas deste laboratório são adicionais e executadas pelo script próprio.

O benchmark repete uma composição simples já preparada e recodifica a 640×360, 30 fps, H.264 veryfast/CRF 18 e AAC. Não representa projetos com várias fontes, 4K, VFR, MOV de celular ou múltiplas camadas. Mesmo neste caso simples, trinta minutos exigiram cerca de cinquenta segundos: a preparação integral permanece uma opção inicial para incrementos controlados, sem promessa de interação imediata após editar. Medir fontes reais antes de fixar perfil/cache e de generalizar a estratégia.

## Achados e implicações para implementação

1. **Erros são eventos:** mídia ausente emite EncounteredError, mas o estado Error pode ser transitório. O coordenador deve registrar falhas por evento, sem depender de polling desse estado.
2. **Inicialização tem custo:** criar o motor fora do caminho crítico dos comandos e mostrar preparação acessível; a prova o inicializa antes de abrir os controles. A partida de 27,3 segundos no portátil torna essa separação obrigatória para o incremento 1.
3. **Callbacks nativos:** encaminhar notificações ao Dispatcher; nunca executar Stop/Dispose dentro de callback nativo. A prova aguarda parada em thread de trabalho e remove assinaturas ao fechar.
4. **Busca precisa permanece aberta:** preservar a previsão de índice VFR/extração de quadros no incremento 4. Não converter a taxa fixa da prévia em semântica de edição da origem.
5. **Foco:** desabilitar comandos durante operações pode deslocar o foco; a prova restaura o elemento de origem. O teste do laboratório com leitor de telas foi aprovado pelo usuário; repetir a validação após integrar ao editor.
6. **Diagnósticos nativos:** execução fora da tela registrou SetThumbNailClip e mensagens de decoder/conversor durante buscas/paradas. As verificações de conteúdo passaram, mas esses diagnósticos e o comportamento com janela visível/GPU precisam ser reavaliados. Não concluir estabilidade em qualquer driver.
7. **Arquitetura:** manter preparação e player atrás de contratos da Application; o laboratório não deve virar um serviço de produção sem separar responsabilidades, estados e gerenciamento de cache.

A tentativa de testar atalhos via controle remoto foi interrompida pela detecção de entrada concorrente do usuário. Somente a exposição dos controles e o layout foram inspecionados por essa via. Posteriormente, em 01/11/2026, o usuário informou: “testei a parte com o leitor tá ok.” Esse relato registra a aprovação manual com leitor de telas no laboratório; a versão do leitor e os passos individuais executados não foram informados. Não estender essa aprovação à escuta do conteúdo audiovisual, à precisão de quadros ou a outras máquinas.

## Percurso manual para reprodução e regressão

Teste com leitor de telas aprovado e saída de áudio confirmada pelo usuário. O roteiro abaixo permanece como referência para repetir a validação após a integração; o relato de áudio audível não certifica sincronismo físico ou cada passo do roteiro. Máquina limpa ainda precisa de confirmação.

1. Abrir `-Manual` com NVDA ativo. Verificar foco inicial, Tab/Shift+Tab, nomes, Alt+4 e ausência de armadilha no host de vídeo.
2. Selecionar `origem.mp4` e acionar Abrir. Ouvir o tom e o pulso, confirmar vídeo, pausar/retomar por botão usando Espaço. A posição deve atualizar sem fala contínua.
3. Digitar 3 no destino, Buscar e Consultar posição. O campo deve aceitar edição normalmente, a imagem deve mudar sem retomar o áudio e o foco retornar ao botão.
4. Abrir MOV e WAV; alternar fontes e verificar ausência de áudio residual. Abrir foto e confirmar imagem estática.
5. Abrir `previa.mp4`: ouvir os dois tons, observar a mudança de cor e pulso aos 2,7 s e foto silenciosa aos 4 s. Confirmar anúncio único no término.
6. Fechar durante preparação e reprodução; reabrir e confirmar ausência de processos/áudio residuais.
7. Repetir a pasta portátil em Windows x64 sem SDK/VLC/FFmpeg instalados. Registrar máquina, NVDA, dispositivo de áudio, resultado e problemas encontrados.

Não há solicitação de mídia pessoal: as amostras são sintetizadas localmente. Os originais do usuário não são lidos.

## Dependências e referências

As dependências VideoLAN declaram LGPL-2.1-or-later em seus metadados NuGet. A distribuição experimental inclui os binários do pacote; distribuição pública exige conservar os avisos e atender às condições das versões/plugins efetivamente incluídos. A origem e os avisos de FFmpeg seguem `docs/media-tools.md`; o script copia LICENSE e README da ferramenta para a pasta portátil. Este laboratório não define a licença do SonarFrame nem é uma release pública.

- [Integração WPF e limitação de airspace](https://docs.videolan.me/libvlcsharp/docs/getting_started.html)
- [API do MediaPlayer](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)
- [Filtros FFmpeg](https://ffmpeg.org/ffmpeg-filters.html)
