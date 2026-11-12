# Latência até a saída nativa da composição

Branch de trabalho: `feat/reproducao-e-previa`, sem criar branch. Continuação do plano de recomposição incremental; preservar as alterações locais anteriores.

## Escopo e rastreabilidade

RF02 (edição não destrutiva e histórico), RF03 (volume e camadas de áudio), RF05 (tempo racional, busca e sincronização), RNF01 (teclado, foco, responsividade e anúncios). Preservar o limite de cache de 2 GB do plano de reprodução, as mídias originais e o JSON persistido. Exportação permanece fora desta entrega.

1. Instrumentar resolução/consulta, criação de processos, processamento de áudio/vídeo, montagem e inicialização/saída do LibVLC. Diferenciar comando concluído de saída apresentada. Não atribuir precisão física aos contadores do motor.
2. Medir o pipeline anterior com AAC integral, em séries sequenciais de 30, 180 e 600 segundos, CFR e VFR, usando a janela WPF e o player real. Comparar quatorze cenários por combinação.
3. Comparar A–H antes da escolha: cache atual, identidade por conteúdo, segmentação, reprodução sob demanda, montagem, níveis de cache, proxies e preparação ociosa. Matriz preliminar apresentada na conversa; consolidar resultados em `resultado-latencia-reproducao.md`.
4. Validar montagem MOV H.264 + PCM float por cópia dos fluxos existentes, sem recodificação AAC global. MOV preserva base de áudio de 48 kHz; não alterar cortes ou a estratégia visual já validada. Medir o aumento de armazenamento, respeitando o mesmo teto.
5. Verificar equivalência integral frio/quente, cópia exata do PCM, junções, cancelamento/concorrência, foco e edição durante preparação. Suprimir somente anúncios de cancelamentos internos; preservar erros e cancelamento explícito.
6. Repetir os cenários após a mudança, executar build/suíte, prévia nativa, matriz de formatos e publicar pelo script oficial em `artifacts/desktop`.

## Decisões por camada

- Core e Application: contratos e domínio preservados.
- Infrastructure: diagnóstico por execução assíncrona e substituição de duas etapas globais (AAC e remux) por um remux dos fluxos compatíveis. Cache intermediário e validação de duração permanecem.
- Desktop: observação dos incrementos dos contadores nativos após retomar, encerrada antes do descarte do player. Registro assíncrono separado do anúncio de estado. Nenhuma região, controle ou atalho novo.
- Testes: benchmark opt-in `medir-latencia.ps1`, amostras e resultados em `.tools/test-artifacts/latencia-reproducao`.

## Critérios de conclusão

Zero renderizações ao repetir composição ou mover cursor; nenhuma recodificação AAC na montagem completa; equivalência audiovisual e testes aprovados; redução medida após edições. A meta de 60% para cenários médios é avaliada pelos resultados, sem presumir aprovação. Validação falada com NVDA e máquina limpa são aceite externo.

Implementação, matriz A–H, 84 pares de medições, testes, publicação e limitações registrados em [resultado-latencia-reproducao.md](resultado-latencia-reproducao.md). A meta de 60% foi atingida em 15 dos 20 cenários médios de edição; a primeira preparação continua sendo um gargalo e teve regressões medidas. Aceite humano permanece separado dos resultados automatizados.
