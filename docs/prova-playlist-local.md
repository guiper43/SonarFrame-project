# Prova inicial de playlist local

## Resultado e decisão

A sequência local simples não passou pelo critério de continuidade do áudio neste ensaio. Os segmentos entregaram a quantidade correta de amostras, mas houve uma descontinuidade positiva de aproximadamente 119–124 ms entre itens, nos timestamps agendados pelo LibVLC. O controle contínuo ficou dentro do arredondamento de microssegundos.

Isso rejeita a integração direta desta configuração como substituta do MOV completo. Não demonstra impossibilidade de toda solução baseada em segmentos, nem mede a lacuna acústica de um dispositivo real. O próximo candidato de transporte passa a ser uma entrada audiovisual contínua, mantendo o cache atual; a preparação direta de seleções permanece um incremento independente.

Continuação: [prova de entrada audiovisual contínua](prova-entrada-continua.md), com mux único e fornecimento progressivo de dados.

## Método reproduzível

Laboratório em `.tools/prova-segmentos`, sem alterações no código da aplicação. LibVLC 3.0.24 da distribuição utilizada pelos testes, com carregamento por LibVLCSharp 3.10.2. A API nativa `libvlc_media_list_player_*` foi chamada por P/Invoke apenas no laboratório: o assembly gerenciado instalado não expõe `MediaListPlayer`. Essa integração acrescentaria manutenção se fosse levada ao produto.

`preparar.ps1` utiliza a amostra `prototipo.mov` do laboratório de latência anterior. Produz um controle de três segundos, H.264 com GOP de um segundo e sem B-frames, e PCM float mono a 48 kHz; o coletor solicita saída estéreo ao LibVLC. A inspeção posterior por FFprobe corrigiu a descrição anterior que chamava o arquivo de estéreo. Para cada segundo, produz NUT de vídeo e NUT de áudio separados; o áudio é delimitado por processamento PCM para obter exatamente 48.000 amostras por canal. Combina cada par por cópia em um MOV audiovisual. É uma amostra controlada com os mesmos codecs/organização dos intermediários, não uma execução do preparador de projeto nem uma prova de VFR/camadas.

`executar.ps1` compila `Prova.cs` com `Add-Type` e executa três pares alternados: lista com um arquivo contínuo e lista com três segmentos. Todos os arquivos já estão disponíveis. Usa callbacks de áudio em vez de saída física, desabilitando o vídeo para isolar a continuidade temporal de áudio. O callback `drain` aguarda o instante final agendado, usando o relógio nativo; não libera a transição enquanto há áudio simulado pendente. Delegates permanecem vivos até o descarte dos recursos nativos.

A lacuna entre dois buffers é calculada como:

`(PTS_atual - PTS_anterior) / 1000 - amostras_anteriores / 48`

PTS está em microssegundos; o resultado é expresso em milissegundos. O coletor verifica 144.000 amostras por canal no total e um `drain` por item. Não interpretar a entrega antecipada de um callback como instante de som no hardware.

Reexecução, a partir da raiz:

```powershell
./.tools/prova-segmentos/preparar.ps1
./.tools/prova-segmentos/executar.ps1
```

Arquivos brutos: `completo-1.json` a `completo-3.json`, `segmentos-1.json` a `segmentos-3.json` e `resumo.json`, na mesma pasta. Os resultados abaixo correspondem à reexecução com verificações de integridade habilitadas. Ensaios preliminares de ajuste do coletor não integram a tabela.

## Medições

| Rodada | Amostras por canal, em cada variante | Maior diferença do controle | Fronteira 1 da lista | Fronteira 2 da lista |
|---|---:|---:|---:|---:|
| 1 | 144.000 | 0,000667 ms | 120 ms | 121 ms |
| 2 | 144.000 | 0,000667 ms | 124 ms | 119 ms |
| 3 | 144.000 | 0,000667 ms | 124 ms | 120 ms |

Não houve perda na contagem de amostras; isso, sozinho, não garante áudio contínuo. As descontinuidades aparecem apesar de não existir espera por renderização ou disponibilidade dos próximos arquivos. A mediana das seis lacunas é aproximadamente 120,5 ms. As diferenças do controle são inferiores a uma amostra de áudio e compatíveis com arredondamento de timestamps.

As primeiras entregas de callbacks ficaram entre aproximadamente 17 e 26 ms nesta reexecução. **Esses valores não são comparáveis ao tempo até saída nativa do benchmark WPF**: a prova usa cache pronto, não apresenta vídeo e substitui a saída de áudio pelo coletor.

## Limites e próximos critérios

- Não houve captura de loopback, escuta humana ou medição física do áudio.
- Não foram demonstrados vídeo contínuo, VFR, B-frames, busca, seleção, camadas ou prefetch. O pré-requisito de continuidade do áudio já falhou nesta configuração.
- Não foi demonstrado consumo direto de NUT pelo LibVLC; foi demonstrada a combinação de fluxos NUT em segmentos MOV reconhecidos pelo motor.
- Três pares são uma triagem de viabilidade, não uma caracterização estatística de desempenho.
- Uma entrada única deve primeiro demonstrar mux contínuo, PTS/DTS coerentes, espera por dados sem falso EOF e cancelamento que desbloqueia leitores. Apenas ligar arquivos a callbacks ou pipes não resolve essas condições.
- O pipeline publicado permanece como referência de correção. Nenhuma playlist foi integrada ao Desktop e a preparação direta de seleções ainda não foi implementada.

Referências: [sequenciamento nativo do LibVLC](https://videolan.videolan.me/vlc-3.0/group__libvlc__media__list__player.html), [contratos de callbacks de áudio](https://videolan.videolan.me/vlc-3.0/group__libvlc__media__player.html). Os resultados quantitativos são locais, não alegações extraídas dessas referências.

## Validação

Prova concluída com contagem de amostras e término dos itens verificados. `build.ps1` passou sem avisos/erros, com 81 testes gerais e 30 testes Desktop aprovados; `testar-previa-desktop.ps1` aprovou cinco testes nativos; `validar-reproducao.ps1` aprovou os dez formatos. Não houve nova publicação: o código do aplicativo permaneceu inalterado nesta etapa de laboratório.

Revisão pelos critérios de `code-reviewer`: laboratório isolado, sem contrato novo no produto; **não aprovar a integração da playlist simples** enquanto persistirem as descontinuidades demonstradas. Não foram esgotadas todas as opções de buffering do motor; qualquer variante futura precisa repetir os mesmos critérios, sem trocar precisão por menor espera.
