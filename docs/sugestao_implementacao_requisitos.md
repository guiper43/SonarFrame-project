# Sugestão de implementação e requisitos — sonarFrame

## 1. Objetivo

Este documento consolida uma sugestão de implementação para o sonarFrame com base nos requisitos aprovados na ERS v0.5, mantendo foco em acessibilidade por teclado, compatibilidade com NVDA e edição não destrutiva.

## 2. Requisitos consolidados

### 2.1 Requisitos funcionais (RF)

- **RF01 — Importação e biblioteca de mídias**
  - Importação via botão **Adicionar**, com guias por tipo.
  - Suporte inicial a `.mp4`, `.mov`, `.jpg`, `.jpeg`, `.png`, `.mp3`, `.wav`, `.m4a`, `.ogg` e `.srt`.
  - Inserção explícita na linha do tempo (importar não adiciona automaticamente).

- **RF02 — Corte, divisão, junção e organização**
  - Edição por seleção temporal.
  - Divisão e reordenação por teclado.
  - Histórico com desfazer/refazer.
  - Operações não destrutivas sobre os arquivos de origem.

- **RF03 — Áudio, mixagem e organização**
  - Controle separado entre áudio original e faixas adicionadas.
  - Volume por fonte e por trecho entre 0% e 200%.
  - Sobreposição de múltiplas fontes com prévia sonora.

- **RF04 — Legendas, imagens, textos e efeitos**
  - Legendas por importação SRT ou geração opcional por IA.
  - Edição contextual de legendas com preservação de foco e posição.
  - Elementos visuais com posições predefinidas e regras de tamanho.
  - Transições fechadas para a primeira versão e controles de fade.

- **RF05 — Navegação temporal e seleção**
  - Navegação por quadro, segundo e dez segundos.
  - Seleção com Shift, navegação para início/fim e anúncio de posição.
  - Preservação de sincronização entre áudio e vídeo nos cortes.

- **RF06 — Exportação**
  - Saída em MP4 com 720p e 1080p.
  - Orientação 16:9 ou 9:16 com escolha obrigatória.
  - Opções de enquadramento, nome/pasta de destino e progresso acessível.

- **RF07 — Pendente**
  - Requisito reservado, sem comportamento definido na versão atual.

### 2.2 Gerenciamento de projetos (GP)

- Tela inicial com Novo projeto, Meus projetos, Abrir projeto existente e Configurações.
- Persistência em SQLite com referências às mídias originais.
- Salvamento manual (`Ctrl + S`) e automático a cada dois minutos quando houver alterações.
- Recuperação de sessão após fechamento inesperado.

### 2.3 Requisitos não funcionais (RNF01)

- Uso integral por teclado.
- Navegação por regiões com atalhos diretos (`Alt + 1` a `Alt + 5`, `F6`, `Shift + F6`).
- Menus contextuais acessíveis e restauração consistente de foco.
- Exposição de controles por UI Automation e validação contínua com NVDA.

## 3. Sugestão de implementação por etapas

### Etapa 1 — Fundação acessível do aplicativo

- Estruturar aplicação WPF com regiões principais (biblioteca, propriedades, linha do tempo, prévia e comandos de projeto).
- Implementar modelo de foco e atalhos globais/contextuais.
- Criar sistema de mensagens recentes e notificações acessíveis.

### Etapa 2 — Núcleo de projeto e persistência

- Implementar criação, abertura, listagem e salvamento de projetos.
- Definir esquema SQLite inicial com versionamento de estrutura.
- Tratar mídias ausentes com fluxo de relocalização.

### Etapa 3 — Biblioteca e montagem inicial

- Implementar importação com validação por tipo e codec.
- Disponibilizar prévia por item na biblioteca.
- Permitir inserção explícita e reordenação inicial na linha do tempo.

### Etapa 4 — Edição temporal e operações de conteúdo

- Implementar navegação RF05 e seleção de trechos.
- Implementar corte, divisão, junção e desfazer/refazer.
- Garantir sincronização audiovisual nas operações de corte.

### Etapa 5 — Áudio, elementos visuais e legendas

- Adicionar mixagem e volume por faixa/trecho.
- Implementar legendas SRT e edição contextual.
- Implementar imagens, textos, posições e efeitos aprovados.
- Habilitar IA como funcionalidade opcional e desligada por padrão.

### Etapa 6 — Exportação e estabilização

- Implementar fluxo de exportação MP4 com opções obrigatórias.
- Exibir progresso consultável e ações de conclusão.
- Validar critérios de aceitação e testes de usabilidade com pessoas cegas.

## 4. Prioridades recomendadas

1. **Acessibilidade estrutural primeiro** (RNF01 + navegação base).
2. **Fluxo completo mínimo** (criar projeto → importar → inserir → salvar → exportar).
3. **Edição avançada e IA opcional** após base estável.
4. **Validação contínua com NVDA e teclado** em todas as etapas.

## 5. Critérios de pronto (visão de implementação)

O fluxo será considerado implementado quando for possível, exclusivamente por teclado e com NVDA:

- Criar/abrir projeto.
- Importar e organizar mídias sem inserção automática na linha do tempo.
- Editar por navegação temporal, corte/divisão/reordenação e ajustes de áudio.
- Trabalhar com legendas, elementos visuais e efeitos aprovados.
- Salvar, recuperar sessão e exportar com feedback acessível.

## 6. Referência base

- Documento de requisitos: `docs/sonarFrame_ERS_v05.md` (ERS v0.5).
