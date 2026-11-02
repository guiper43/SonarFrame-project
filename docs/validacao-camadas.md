# Validação da reprodução com camadas

01/11/2026. Incremento 3 da feature de reprodução; sem exportação e sem mudança no formato dos projetos.

## Implementado

- Composição de camadas de vídeo, imagem e áudio em seus intervalos, incluindo recortes na origem e volumes individuais.
- Convenção visual confirmada pelo usuário: centralização sem corte, proporção e transparência preservadas, última camada acima. O quadro segue a primeira mídia visual principal; lado maior de 640 pixels, 30 fps.
- Mix sem normalização automática ou limitador. Ganhos altos podem exceder a faixa de saída e distorcer; a codificação AAC também altera a forma de onda.
- Botão da composição com Reproduzir ou Pausar conforme o estado, consulta das camadas ativas, revalidação das fontes antes de retomar e nova preparação quando necessário.
- Imagem decodificada da prévia ao pausar, parar ou navegar depois da preparação. Última busca prevalece; End conserva a duração no cursor e mostra o último quadro disponível.
- Cache de uma montagem por janela, publicação atômica, limite de 2 GB, descarte ao fechar e remoção de resíduos identificados de sessões encerradas. Caches em uso são protegidos por arquivo exclusivo.

## Verificações automatizadas

Compilação Release sem avisos ou erros. Os 23 testes WPF passaram, incluindo os dois testes com LibVLC nativo. Dos 68 testes de núcleo/aplicação/infraestrutura, 67 passaram na rodada completa; o teste restante de saturação passou após corrigir a medição para observar picos float estéreo acima da faixa, sem depender da conversão para mono inteiro. Nenhuma alteração de produção foi necessária para essa correção da medição.

Cobertura audiovisual com FFmpeg/FFprobe real:

- Conteúdo após recorte fora de keyframe, sequência vídeo/foto/áudio, silêncio e volume zero.
- Entrada e saída das camadas, ordem de empilhamento, transparência e barras transparentes para preservar proporção.
- Frequências de áudio separadas: ganho relativo de 50% e 200%, ausência de normalização quando outra camada entra, silêncio fora do intervalo.
- Conteúdo visual e duração depois da exclusão de um intervalo da composição.
- Rotação de MOV por matriz de exibição, vídeo sem áudio, Unicode e espaços em caminhos.
- Invalidação por alteração externa e erro por fonte ausente.
- Cancelamento durante preparação de uma composição longa, nova tentativa, descarte do cache e liberação do arquivo original.
- Extração PNG no destino e no fim da composição.

Cobertura WPF adicional: navegação pausa, conserva o fim lógico, não cria histórico, invalida buscas antigas e revalida o cache antes de retomar. O teste nativo confere imagem pausada, navegação ao fim e retorno à reprodução.

Os temporários desta execução ficaram em `.tools/test-artifacts/temporarios`, por meio de TEMP/TMP do processo de testes. A publicação para o usuário permanece exclusivamente em `artifacts/desktop`.

## Validação manual e pendências

Conferir com NVDA: localizar Reproduzir / Pausar composição pelas ações da timeline, iniciar, pausar, consultar posição/camadas e remover/desfazer um elemento. Conferir com mídias reais a entrada e saída de áudio e sobreposições.

Esta entrega não conclui os incrementos 4 e 5: falta índice VFR e apresentação precisa dos quadros da origem, matriz ampliada de formatos reais, medições de projetos longos e validação portátil em máquina limpa. A imagem pausada é um quadro da prévia de 30 fps, não uma garantia de precisão da origem. O término automático de seleção ainda depende da atualização de 100 ms. Os testes WPF não substituem a validação manual do leitor de tela.


Atualização: as pendências de implementação de navegação e reprodução delimitada foram tratadas na etapa final. Consulte [a validação final](validacao-final-reproducao.md) para resultados atuais e critérios de aceite externo.
