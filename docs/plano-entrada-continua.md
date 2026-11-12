# Implementação da reprodução contínua com recuperação

## Escopo autorizado

Integrar a alternativa validada em `prova-entrada-continua.md`: uma mídia NUT audiovisual progressiva, reserva inicial, preparação antecipada limitada e recuperação quando a produção não acompanha a reprodução. Manter o cache canônico separado, o domínio racional, os atalhos e os contratos existentes. Publicar para validação em `artifacts/desktop`.

## Requisitos e critérios

- RF01.08: preservar a prévia da biblioteca e sua matriz de formatos.
- RF02.02, RF02.04 e RF02.09: preservar cortes, divisões, equivalência dos blocos e arquivos originais.
- RF03 e RF04: manter mixagem e camadas no renderizador existente.
- RF05: iniciar pelo cursor/seleção; busca e edição invalidam a geração anterior; pausa, retomada e conclusão mantêm coordenadas globais.
- RNF01: recuperação e erros acessíveis, sem mover o foco nem anunciar detalhes de cache. Aceite com NVDA permanece externo.
- Critérios técnicos: reprodução antes do fim da preparação; reserva limitada; cancelamento desbloqueia processos/leitores; nenhum EOF temporário; cache total limitado a 2 GB; integridade de áudio e vídeo; regressões gerais/nativas e dez formatos aprovados.

## Divisão por camada

- Core: nenhuma alteração de modelo ou persistência.
- Application: contrato opcional de sessão/fluxo e eventos de preparação/retomada. Implementações antigas continuam usando a preparação de arquivo completo.
- Infrastructure: reutilizar os blocos por identidade, preparar a partir do cursor e antecipar uma janela limitada. Um mux FFmpeg lê os pares por entradas locais que aguardam sua disponibilidade. Saída NUT armazenada progressivamente em arquivo temporário limitado; leituras aguardam dados ou cancelamento. Sessão detém os recursos e a proteção do cache até descarte.
- Desktop: adaptar o fluxo à entrada gerenciada LibVLC, controlar reserva e pausa/retomada, manter foco e cancelar a geração em busca/edição/fechamento. Não copiar P/Invoke de laboratório para o produto.

## Incrementos verificáveis

1. Contrato e produtor: stream progressivo, blocos reutilizados, limites e cancelamento; teste com mídia real e comparação do mux.
2. Adaptador nativo: abertura, pausa, retomada e recuperação; testes com atraso controlado e cancelamento.
3. Editor: cursor/seleção, navegação, edição e anúncios; regressões de teclado e fluxo de biblioteca.
4. Revisão `code-reviewer`, build/testes, matriz nativa e publicação única para validação manual.

## Validação

Implementação integrada na branch `feat/reproducao-e-previa`. A prova de laboratório permanece uma evidência separada do aceite da aplicação.

## Funcionamento implementado

- Reserva inicial de três segundos, pausa preventiva quando restam até dois segundos e retomada com cinco segundos disponíveis, limitados ao restante da reprodução. O progresso do mux FFmpeg determina a reserva; terminar de renderizar um bloco, por si só, não libera a retomada.
- Preparação de blocos até quinze segundos à frente da posição. O cache de blocos ocupa até aproximadamente 1 GB; o arquivo progressivo tem limite próprio inferior a 1 GB, preservando o orçamento total de 2 GB. Atingir o limite gera erro acessível, sem apagar mídias originais.
- Um servidor restrito ao endereço de loopback fornece somente os arquivos registrados para a sessão, por rotas aleatórias. Ele atende leituras HTTP com faixa de bytes e aguarda os blocos. A tentativa com named pipes não preservou a sequência completa e foi substituída. Não há conexão externa nem nova dependência NuGet.
- Um único mux FFmpeg produz NUT com vídeo e áudio PCM. A entrada gerenciada do LibVLC lê um arquivo progressivo: ausência temporária de bytes aguarda dados; cancelamento desbloqueia a leitura. O acesso aleatório é limitado aos bytes já escritos. Navegação temporal inicia outra sessão no cursor solicitado.
- A recuperação pausa e retoma o mesmo player. Pausa voluntária invalida a retomada automática. Cancelar, trocar de posição/composição ou fechar descarta a sessão, processos, leitor e arquivos temporários. A retomada manual que aguarda dados também pode ser cancelada.
- Cursor e seleção usam tempos racionais. A seleção é preparada diretamente, sem montar primeiro a composição inteira. O caminho anterior de arquivo completo continua disponível para implementações do contrato que não oferecem a entrada contínua.

## Evidências automatizadas

- Teste com mídia real compara os hashes de todos os quadros e os bytes PCM com a montagem completa, incluindo VFR, camada e corte fracionário. Uma seleção fracionária é comparada com o recorte do PCM de referência.
- Testes de aplicação cobrem recuperação, pausa voluntária, descarte durante espera, cancelamento da retomada manual e reativação do monitor quando a retomada ocorre antes de completar a reserva da recuperação anterior.
- Testes WPF/LibVLC atrasam de forma controlada a preparação da segunda mídia: verificam pausa nativa, preservação de posição e foco, retomada no mesmo player sem mudo e cancelamento seguido de reabertura.
- `./build.ps1`: build Release sem avisos ou erros; 117 testes aprovados (30 Desktop e 87 gerais). Oito testes opt-in ignorados nessa etapa: sete nativos executados no script específico e um benchmark não solicitado nesta entrega.
- `./testar-previa-desktop.ps1`: sete testes nativos aprovados, nenhum ignorado, incluindo recuperação e cancelamento.
- `./validar-reproducao.ps1`: dez formatos aprovados — JPG, JPEG, PNG, MP4, MOV, MP3, WAV, M4A, OGG e Opus/OGG. Resultado local em `.tools/test-artifacts/validacao-reproducao/resultado.json`.
- `git diff --check`: sem erros de whitespace.
- `./run-desktop.ps1 -Publish`: publicação win-x64 self-contained concluída em `artifacts/desktop`, substituindo a versão anterior.

## Revisão e limites do aceite

Revisão aplicada conforme `code-reviewer`: contratos entre camadas, invalidação, cancelamento, descarte, leitura progressiva, limite de recursos, argumentos de processos sem shell, rotas restritas, foco e anúncios. Ajustes realizados: aguardar a cópia do mux também no caminho de falha antes de liberar o arquivo de escrita; cancelar a espera antiga de recuperação ao pausar/retomar manualmente; manter o texto e nome acessível “Pausar” enquanto há retomada automática pendente.

O aceite manual com NVDA e a escuta em hardware permanecem necessários. Os testes não estabelecem garantia de latência para toda máquina ou mídia, nem substituem a comparação auditiva. Máquinas que produzam dados lentamente podem apresentar pausas de recuperação. Não foi adotado armazenamento circular: composições que excedam os limites da sessão recebem erro e precisam ser reduzidas para reprodução.

Veredito da revisão: **Pode aprovar**. Não restaram achados relevantes nos caminhos revisados após os ajustes e suas regressões automatizadas. A publicação é destinada à validação manual; a distribuição estável depende do aceite descrito acima.
