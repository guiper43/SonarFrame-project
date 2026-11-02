---
name: sdd
description: >-
  Spec-Driven Development (SDD) workflow for sonarFrame. Use when planning a new feature,
  breaking down requirements from docs/sonarFrame_ERS_v05.md into incremental deliverables,
  implementing against formal acceptance criteria (RF/RNF/CA), or auditing adherence of an
  implementation against the specification and feature plan.
---

# Spec-Driven Development (SDD) — sonarFrame

Esta skill define o fluxo de **Desenvolvimento Guiado por Especificação (SDD)** para o `sonarFrame`, garantindo rastreabilidade direta entre a Especificação de Requisitos de Software (`docs/sonarFrame_ERS_v05.md`), os planos de feature (`docs/plano-*.md`), o código e os testes automatizados.

## 1. Fonte da verdade

1. **Especificação oficial**: `docs/sonarFrame_ERS_v05.md` é a fonte primária de requisitos funcionais (`RF-xx`), requisitos não funcionais (`RNF-xx`), critérios de aceitação (`CA-RFxx-yy` / `CA-RNFxx-yy`) e decisões técnicas (`DT-xx`). Nunca usar o `README.md` como substituto da ERS para definir escopo ou regras de negócio.
2. **Invariantes do projeto**: preservar sempre as regras de `AGENTS.md` (domínio em português brasileiro, zero comentários no código, `TempoMidia` racional de 64 bits sem ponto flutuante, `Projeto.Validar()` como única fonte de invariantes, compatibilidade JSON/SQLite e acessibilidade 100% por teclado/NVDA).

## 2. Fase 1 — Planejamento da Feature (`docs/plano-<feature>.md`)

Antes de iniciar a codificação de uma nova feature ou épico, estruturar ou validar o documento de plano contendo:

1. **Objetivo e fronteira de escopo**:
   - O que entra nesta feature e o que fica explicitamente fora de escopo (evitando acoplamento prematuro com etapas futuras).
2. **Mapeamento de requisitos da ERS**:
   - Tabela ou lista ligando cada requisito (`RF-xx`, `RNF-xx`) e critério de aceitação (`CA-...`) que será atendido ou parcialmente atendido.
3. **Decisões arquiteturais por camada**:
   - `SonarFrame.Core`: tipos de valor, records e invariantes em `Projeto.Validar()`.
   - `SonarFrame.Application`: contratos, coordenação assíncrona, estado e comandos em `SessaoEdicao`.
   - `SonarFrame.Infrastructure`: integração externa (`ffprobe`, `ffmpeg`, `SQLite`, cache em disco, limites de memória/disco).
   - `SonarFrame.Desktop`: atalhos de teclado, foco explícito, `AutomationProperties`, anúncios em região viva (`Situacao`) e ausência de bloqueio da thread de UI.
4. **Divisão em Incrementos Verificáveis (Incremento 0..N)**:
   - Cada incremento deve ter entregáveis claros, testes automatizados próprios e critérios objetivos de conclusão (*Definition of Done*).

## 3. Fase 2 — Execução Incremental

Para cada incremento planejado:

1. **Implementar de dentro para fora**: `Core` -> `Application` -> `Infrastructure` -> `Desktop`.
2. **Escrever testes que provem os critérios de aceitação**:
   - Testes unitários e de contrato para domínio/aplicação (`tests/SonarFrame.Tests`).
   - Testes com mídias/ferramentas reais quando aplicável (`[MidiasReaisFact]`, `[PreviaNativaFact]`).
   - Testes de integração WPF em thread STA validando foco, atalhos, estados de botões e anúncios (`tests/SonarFrame.Desktop.Tests`).
3. **Validar build e suíte completa**:
   - Rodar `./build.ps1` (com `-m:1`, `TreatWarningsAsErrors=true`, zero avisos e zero erros).
   - Se envolver prévia/reprodução/mídia, rodar `./testar-previa-desktop.ps1` e `./validar-reproducao.ps1`.
   - Publicar a versão de validação em `artifacts/desktop` via `./run-desktop.ps1 -Publish`.

## 4. Fase 3 — Auditoria de Aderência (Pente Fino SDD)

Quando solicitado a verificar a aderência ou concluir uma feature, confrontar linha por linha o código com a ERS e o plano da feature, classificando cada requisito/critério em:

- **Atendido (Automatizado)**: implementado e coberto por teste automatizado reproduzível (citar arquivo/teste).
- **Parcial / Com Ressalva**: implementado, mas com limitação técnica conhecida, desvio de meta de performance ou comportamento de borda pendente (citar arquivo/linha e evidência).
- **Pendente de Aceite Externo**: depende de validação humana/ambiente externo que a suíte local não substitui (ex.: percurso completo falado no NVDA real, teste portátil em máquina limpa).
- **Não Atendido**: requisito previsto no escopo da feature que ainda não foi implementado ou apresenta bug/divergência textual.

Nunca declarar um critério de aceite externo (como teste real com leitor de tela NVDA ou execução em máquina limpa) como aprovado apenas com base em testes automatizados locais.
