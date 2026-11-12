# Recomposição incremental: investigação e otimização

Continuação: [latência até a saída nativa](plano-latencia-reproducao.md), que investiga e elimina a etapa AAC global identificada nesta entrega. Os resultados abaixo preservam a medição histórica anterior.

Branch atual de reprodução; preservar alterações de timeline já validadas.

## Escopo e critérios

RF02 (cortes, divisão, reordenação, histórico), RF05 (precisão e sincronização), RNF01 (interface responsiva e anúncios proporcionais), cache de 2 GB do plano de reprodução. Nenhuma mudança no domínio persistido, formato do projeto ou motor de reprodução.

1. Instrumentar o comportamento atual e registrar baseline dos doze cenários solicitados, com CFR/VFR. Contar consultas, hits, misses e renderizações; medir preparação e montagem. Identificar explicitamente fases que o grafo FFmpeg atual executa conjuntamente, sem inventar tempos exclusivos.
2. Comparar identidade audiovisual com identidade de edição. Investigar segmentação ancorada na origem que preserve a grade global de quadros; fusão de cortes contíguos sem alteração audiovisual; camadas relativas a cada segmento e perfil de saída nas chaves.
3. Implementar somente alternativas com ganho demonstrado. Medir custo da montagem final e avaliar isolamento de áudio/vídeo sem introduzir artefatos de codec nas junções.
4. Testar conteúdo completo, não somente extremidades: fontes, origens, durações, volumes, ordem e intervalos das camadas. Validar quadro e áudio nas fronteiras, cancelamento, fontes alteradas, progresso e histórico.
5. Repetir benchmark nas mesmas mídias, documentar ganhos/limitações, executar build e validações nativas/matriz e publicar em artifacts/desktop.

Achados iniciais: grade global fixa de cinco segundos muda todos os recortes após ripple de um segundo; chave serializa IDs/nomes dos clipes; montagem final copia H.264 mas recodifica AAC integralmente. Cursor/foco/seleção não estão na chave. Mixagem e codificação visual atualmente compartilham o mesmo processo/grafo.

Implementação e análise completas em [resultado-recomposicao-incremental.md](resultado-recomposicao-incremental.md). A solução mantém o preparador existente, usa segmentação por origem compatível com a grade de saída e separa os fluxos intermediários, sem criar outro motor ou alterar projetos persistidos. O custo inicial maior e a codificação AAC final ainda global são compromissos explícitos da versão.
