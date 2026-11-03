# Prova de entrada audiovisual contínua

Este relatório preserva os resultados do laboratório anterior à integração. A implementação posterior, incluindo recuperação quando faltam dados, está descrita em [plano e validação da entrada contínua](plano-entrada-continua.md).

## Resultado e decisão

A entrada única com NUT multiplexado por cópia e demux `avformat` no LibVLC passou na triagem de continuidade com fornecimento suficiente de dados. É a próxima candidata para um protótipo integrado, preservando o cache de vídeo/áudio separado. A integração no aplicativo ainda não foi implementada.

A prova também estabelece um limite: uma interrupção de dois segundos no fornecimento excede a reserva desta configuração e provoca descontinuidade temporal e descarte/repetição de quadros. A contagem e o hash do áudio continuam corretos mesmo nesse caso; **integridade do conteúdo não equivale a apresentação contínua**.

## Método

Laboratório local em `.tools/prova-entrada-continua`, na branch `feat/reproducao-e-previa`. Scripts e dados ficam em `.tools`, fora do pacote Desktop e ignorados pelo Git. O relatório registra o procedimento, mas os arquivos do laboratório não fazem parte de um checkout limpo.

`preparar.ps1` gera uma referência de 12 segundos, 160 × 90, 30 fps, H.264 sem B-frames e com GOP de um segundo, além de PCM float estéreo a 48 kHz. Divide a referência em quatro pares NUT de três segundos, com vídeo e áudio separados. O mux de controle concatena cada fluxo com durações explícitas e combina os dois por `-c copy`. São 360 quadros e 576.000 amostras por canal.

`Prova.cs` mantém uma única mídia e um único player LibVLC por execução. Um único FFmpeg lê os dois manifestos concat e escreve NUT audiovisual em stdout. O produtor usa `-readrate 1` em cada entrada para que o mux demore aproximadamente a duração da mídia: isso permite verificar reprodução anterior ao término, sem atribuir esse tempo artificial ao custo real de remux.

Uma fila com capacidade para quatro blocos de até 64 KiB conecta stdout à leitura nativa. O callback entrega no máximo 32 KiB por chamada, espera quando faltam dados e retorna zero somente ao terminar normalmente. Cancelamento/falha retorna erro. Não há servidor HTTP, AAC intermediário ou concatenação bruta dos bytes dos NUT. O limite de 256 KiB aplica-se à fila gerenciada, não à memória total de FFmpeg, LibVLC ou instrumentação.

A mídia usa `:demux=avformat`, `:avcodec-hw=none` e `:clock-synchro=0`. Na exploração inicial, o demux Matroska nativo não abriu o fluxo não buscável; `avformat` abriu. Matroska acrescentou quantização de timestamps em milissegundos. NUT preservou a precisão dos fluxos. Sem o ajuste de relógio, apareceram correções temporais durante o fornecimento cadenciado. Essas observações justificam a configuração testada; não demonstram impossibilidade de outras configurações.

O código-fonte do [acesso imem no LibVLC 3](https://raw.githubusercontent.com/videolan/vlc/3.0.x/modules/access/imem-access.c) informa capacidade de controle de ritmo conforme a presença de seek e usa atraso padrão. Portanto, não atribuir os resultados apenas a opções `file-caching`/`live-caching`. O experimento não oferece callback de seek.

Áudio e vídeo são observados por callbacks nativos. O áudio é coletado em S16N estéreo, com hash SHA-256, contagem e PTS; o mux e o cache continuam PCM float. `drain` espera o último instante agendado. O vídeo é decodificado em software para RV32, com hash e horário de cada apresentação ao callback. Os buffers visuais ficam vivos até parar o player, evitando liberar memória ainda referenciada pelo motor; essa retenção é instrumentação limitada à amostra, não proposta de armazenamento do produto.

A conferência com a referência MOV detectou um erro no coletor preliminar: ele solicitava FL32 e interpretava oito bytes por amostra estéreo, mas a saída `amem` desta versão entrega S16N, quatro bytes por amostra estéreo. Isso tornava os hashes preliminares inválidos e podia ler além do buffer. O coletor foi corrigido e a série inteira repetida; resultados anteriores à correção não sustentam a conclusão final. A restrição aparece também no [código do amem do LibVLC 3](https://raw.githubusercontent.com/videolan/vlc/3.0.x/modules/audio_output/amem.c). A prova anterior da playlist apenas contava amostras/PTS, sem ler os buffers, e não depende desses hashes.

O player pode repetir a apresentação de um quadro, inclusive ao terminar. Por isso a análise compara os 360 hashes distintos em ordem, exige a sequência original após remover apenas repetições consecutivas e mede também o intervalo entre quadros diferentes. Contar apenas chamadas de vídeo ocultaria congelamentos durante falta de dados.

## Cenários e critérios

Três rodadas alternam sete cenários:

| Cenário | Fornecimento e critério |
|---|---|
| Arquivo | Controle NUT pronto; áudio idêntico à referência e 360 quadros distintos. |
| Fluxo | Mux em stdout; primeiro áudio antes do término do produtor, mesmo conteúdo e precisão temporal do controle. |
| Progressivo | Só o primeiro par existe no diretório lido pelo FFmpeg ao iniciar. Os demais pares são copiados aproximadamente em 1, 4 e 7 segundos. Reprodução deve começar antes do último par estar disponível. |
| Pausa curta | Leitura de stdout suspensa por 250 ms após aproximadamente 2,5 s; continuidade comparada ao controle. |
| Pausa longa | Suspensão de dois segundos no mesmo ponto; caracteriza esgotamento da reserva e recuperação, sem exigir continuidade impossível sem dados suficientes. |
| Cancelamento inicial | Produtor suspenso antes de entregar bytes; cancelar após 500 ms, com leitor bloqueado. |
| Cancelamento ativo | Suspender o produtor após 2,5 s; cancelar após 3 s, já durante reprodução e com leitura bloqueada. |

Os blocos progressivos são cópias atrasadas de fixtures já geradas. Isso demonstra abertura anterior à disponibilidade completa no diretório de entrada, **não mede renderização fria real nem implementa espera por arquivo ausente**. Os próximos blocos chegam antes de o FFmpeg precisar abri-los. Um scheduler de produção ainda precisa garantir esse contrato ou fornecer uma entrada que aguarde a disponibilidade.

O cancelamento deve encerrar a leitura, o produtor, o FFmpeg e o player em menos de dois segundos. Só processos criados pelo laboratório são encerrados. O aplicativo do usuário não é controlado pela prova.

`verificar.ps1` também decodifica referência/MOV e controle/NUT com FFmpeg: compara PCM byte a byte por hash, compara a sequência completa de framemd5, verifica DTS/PTS contínuos em cada fluxo e duração exata de 12 segundos. Reproduz ainda o MOV original no LibVLC e exige os mesmos hashes de áudio S16N e quadros observados no controle NUT. Em seguida verifica os callbacks das três rodadas, inclusive hashes, número de amostras, quadros e cancelamento. As comparações mantêm o mesmo decodificador e formato de saída em cada par.

## Medições finais

Série final com o coletor S16N corrigido; valores mínimo–máximo nas três rodadas:

| Cenário | Primeiro áudio | Quadros distintos | Maior lacuna positiva de PTS do áudio | Maior espera entre quadros diferentes |
|---|---:|---:|---:|---:|
| Arquivo | 49–56 ms | 360 | 0,000667 ms | 44–45 ms |
| Fluxo | 663–1080 ms | 360 | 0,000667 ms | 45–46 ms |
| Progressivo | 616–1087 ms | 360 | 0,000667 ms | aproximadamente 45 ms |
| Pausa de 250 ms | 1075–1094 ms | 360 | 0,000667 ms | 44–45 ms |
| Pausa de 2 s | 592–1090 ms | 355 | 1173–1296 ms | 1280–1285 ms |

Todos os cenários completos entregaram 576.000 amostras por canal, um único `drain` e o mesmo hash de áudio S16N da referência MOV. Nos quatro cenários sem esgotamento prolongado, a sequência visual também coincidiu com a referência. As diferenças de PTS ficaram entre −0,000667 e +0,000667 ms, inferiores a uma amostra de áudio. A precisão dos fluxos multiplexados foi verificada separadamente por FFprobe.

No caso progressivo, o primeiro áudio chegou em 0,62–1,09 s; o último par só ficou disponível por volta de 7 s e o produtor terminou em 11,59–12,09 s. Isso comprova que a reprodução começou antes da disponibilidade e do mux completos neste ensaio. A fila chegou a no máximo quatro blocos nos cenários da série.

Os seis cancelamentos foram solicitados enquanto a leitura estava bloqueada. O encerramento levou 81–96 ms, incluindo desbloqueio do leitor, término do produtor/processo e descarte nativo. Nenhum EOF foi usado para representar uma pausa temporária de dados.

A pausa longa terminou e preservou todas as amostras, mas entregou somente 355 dos 360 quadros distintos, com repetições e uma espera de aproximadamente 1,28 s por imagem diferente. Esse cenário **não passou em continuidade**; faz parte da caracterização negativa, não deve ser contado como reprodução sem falhas.

O verificador de integridade passou: referência MOV e NUT têm PCM float e quadros decodificados idênticos no FFmpeg; os dois também têm saída S16N e sequência visual equivalentes no LibVLC. Não reaproveitar os hashes da série preliminar com o formato incorreto.

## Limites e próximo incremento

- Estes são callbacks e relógios do motor. Não houve captura de loopback, teste acústico, apresentação em uma janela WPF ou validação com NVDA.
- Não extrapolar latência desta fixture pequena para a primeira reprodução de um projeto frio. A preparação do primeiro bloco, inicialização e custos de interface continuam existindo.
- Não foram provados VFR, B-frames, camadas, cortes fracionários arbitrários, busca distante, retomada após edição ou mudança de geração. O áudio sintético é um tom periódico; faltam também sinais/transições mais diversos e mídia real.
- A reprodução usa uma entrada não buscável. Busca temporal precisa de um novo fluxo a partir do cursor ou de outro contrato de acesso. Não simular seek retornando sucesso em offsets não suportados.
- A fila limitada não equivale a política de prefetch. A falta longa de dados exige reserva temporal suficiente e estado explícito de preparação/retomada; desabilitar sincronização não elimina underflow.
- Não houve substituição de cache, mudanças no domínio ou nova dependência de LibVLC nas camadas internas. Não houve nova publicação do aplicativo nesta etapa.

Próximo incremento recomendado: sessão experimental que solicite um intervalo inicial, prepare os próximos blocos com antecedência limitada, cancele por geração e só exponha reprodução quando houver reserva adequada. Antes de integrar como caminho padrão, repetir equivalência com mídia real, cortes fracionários/VFR, busca, falta de dados e saída física. Preparação direta de seleção continua uma entrega independente.

## Reprodução local

```powershell
./.tools/prova-entrada-continua/preparar.ps1
./.tools/prova-entrada-continua/executar.ps1 -Rodadas 3
./.tools/prova-entrada-continua/verificar.ps1
```

Dados brutos: `nut-<cenário>-<rodada>.json`. Resumos: `nut-resumo.json` e `verificado.json`. As explorações preliminares com Matroska e configurações intermediárias não integram a série final. O contrato nativo de leitura está documentado em [LibVLC media callbacks](https://videolan.videolan.me/vlc-3.0/group__libvlc__media.html).

## Validação e revisão

Validação concluída em 03/11/2026: `build.ps1` sem avisos/erros, 81 testes gerais e 30 testes Desktop aprovados; `testar-previa-desktop.ps1` com cinco testes nativos aprovados; `validar-reproducao.ps1` com os dez formatos aprovados. O benchmark completo de 84 cenários não foi repetido: nenhuma alteração foi feita no pipeline do aplicativo nesta etapa.

Revisão com `code-reviewer`: o erro de interpretação do formato de áudio no coletor foi corrigido e as três rodadas foram repetidas. Conferidos limites da fila, vida dos delegates/buffers, descarte e desbloqueio por cancelamento, inexistência de alterações nas camadas do produto e distinção entre integridade e continuidade. Não foram encontrados outros problemas relevantes para o escopo controlado do laboratório.

Veredito: **Pode aprovar a prova de viabilidade**, com os limites registrados. **Não aprovar ainda como substituição do pipeline de produção**: faltam reserva/retomada quando os dados atrasam, busca, integração por geração, mídias mais diversas e validação de saída física/acessível.
