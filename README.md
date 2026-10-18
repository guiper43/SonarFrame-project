# sonarFrame

**Edição de vídeo acessível, com controle completo pelo teclado.**

O **sonarFrame** é um projeto de editor de vídeo para Windows, pensado inicialmente para pessoas cegas e usuárias de leitores de tela. Seu objetivo é tornar tarefas comuns de edição audiovisual — importar, organizar, cortar, legendar, combinar áudios e exportar — possíveis de realizar com autonomia, sem depender do mouse ou de uma linha do tempo exclusivamente visual.

> **Estado do projeto:** especificação funcional concluída para os requisitos RF01 a RF06 e para os fluxos de projetos e acessibilidade. **O software ainda está em desenvolvimento**; as funcionalidades descritas abaixo são requisitos aprovados, não uma indicação de que já estejam implementadas.

## Princípios do produto

- **Acessibilidade desde a arquitetura:** operação integral por teclado, compatibilidade com NVDA e controles expostos por UI Automation.
- **Simplicidade sem perder precisão:** edição temporal quadro a quadro, com deslocamentos maiores quando necessário, sem exigir conhecimento de FPS ou milissegundos.
- **Autonomia de edição:** organização dos conteúdos por listas acessíveis e comandos de teclado, sem depender de arrastar elementos visualmente.
- **Edição não destrutiva:** preservar os arquivos originais e aplicar alterações na composição do projeto.
- **Processamento local:** usar FFmpeg para edição e exportação convencionais, sem exigir conexão com a internet.
- **IA opcional e sob controle:** funcionalidades de IA desabilitadas por padrão; nenhuma sugestão será aplicada sem autorização explícita.

## Funcionalidades planejadas

### Biblioteca de mídias

O botão **Adicionar** reunirá três guias: **Fotos e vídeos**, **Áudios e músicas** e **Legendas e efeitos**. Será possível importar múltiplos arquivos e consultá-los na biblioteca antes de incluí-los na linha do tempo. **Importar uma mídia não a adicionará automaticamente à edição.**

### Linha do tempo e montagem

- Cortar, dividir e unir trechos, com **desfazer** e **refazer**.
- Reordenar vídeos e partes por teclado, inclusive movendo um item para antes, depois, início ou fim.
- Inserir novos conteúdos entre trechos existentes.
- Navegar tanto pelo tempo quanto por uma **lista estruturada de conteúdos**, com nome, tipo, ordem e duração quando disponíveis.
- Manter áudio e vídeo sincronizados nas operações de corte audiovisual.

### Áudio

- Combinar áudio original, músicas e gravações simultaneamente.
- Ajustar volumes independentes entre **0% e 200%**, inclusive por trecho.
- Definir quando cada áudio começa e termina.
- Aplicar **fade-in** e **fade-out** em áudio e ouvir uma prévia das alterações.

### Legendas, textos e imagens

- **Importar legendas SRT** ou **gerá-las por IA**, quando habilitada e configurada.
- Revisar legendas existentes pausando no trecho e abrindo **Editar legenda** pelo menu de contexto.
- Alterar texto, fonte, tamanho, cor, fundo e posicionamento, em uma legenda ou em todas.
- Posicionar imagens, textos e legendas em **nove posições** (superior/centro/inferior × esquerda/centro/direita).
- Sobrepor imagens ao vídeo ou substituir temporariamente sua parte visual, escolhendo entre **preencher por inteiro** e **permitir bordas**.

A primeira versão **não** incluirá criação de legendas do zero por digitação manual. A edição de legendas importadas ou geradas continuará disponível.

### Transições e efeitos

O conjunto inicial terá cinco transições: **Dissolver suavemente**, **Passagem pelo preto**, **Passagem pelo branco**, **Deslizar** e **Zoom de transição**. A duração será selecionável em **Rápida (0,3 s)**, **Normal (0,8 s)** ou **Suave (1,5 s)**.

Os efeitos **fade-in** e **fade-out** também estarão disponíveis para fotos e vídeos, com controles visuais e sonoros independentes. Vinhetas prontas poderão ser importadas como vídeos comuns; não está previsto um gerador automático de intros.

### Projetos e exportação

- Tela inicial com **Novo projeto**, **Meus projetos**, **Abrir projeto existente** e **Configurações**.
- Projetos persistidos em **SQLite**, com referências às mídias originais.
- **Ctrl + S** para salvar; salvamento automático a cada **dois minutos**, quando houver alterações; opção de recuperação após fechamento inesperado.
- Exportação em **MP4**, com **720p** ou **1080p** (resolução padrão).
- Escolha obrigatória entre **16:9** (horizontal) e **9:16** (vertical), sem orientação pré-selecionada.
- Opção de **preencher a tela** ou **permitir bordas**, além de pasta e nome de destino configuráveis.
- Progresso acessível e, ao concluir, opções para **abrir o vídeo** ou **abrir a pasta**.

## Navegação por teclado

A interface será organizada em regiões, com atalhos de acesso direto:

| Atalho | Ação prevista |
|---|---|
| `Alt + 1` | Biblioteca de mídias |
| `Alt + 2` | Propriedades do elemento |
| `Alt + 3` | Linha do tempo |
| `Alt + 4` | Prévia e reprodução |
| `Alt + 5` | Comandos do projeto |
| `F6` / `Shift + F6` | Alternar entre regiões |
| `Tab` / `Shift + Tab` | Navegar entre controles |
| `Shift + F10` ou tecla de aplicações | Abrir menu de contexto |
| `Espaço` | Reproduzir/pausar, quando o foco estiver na linha do tempo ou na prévia |

**Com foco na linha do tempo**, a navegação temporal seguirá estas regras:

| Atalho | Ação prevista |
|---|---|
| `←` / `→` | Retroceder/avançar um quadro |
| `Ctrl + ←` / `Ctrl + →` | Retroceder/avançar um segundo |
| `Page Up` / `Page Down` | Retroceder/avançar dez segundos |
| `Home` / `End` | Ir ao início/fim do vídeo |
| `Shift` + atalhos de navegação | Selecionar o intervalo percorrido |
| `Shift + Home` / `Shift + End` | Selecionar até o início/fim do vídeo |

Os atalhos serão **contextuais**: setas e espaço continuarão com seu comportamento habitual em listas, caixas de texto e demais controles. Durante a reprodução, o NVDA não anunciará continuamente o tempo decorrido; a posição será informada quando houver navegação manual ou consulta explícita.

## Formatos previstos

| Conteúdo | Formatos de entrada |
|---|---|
| Vídeos | `.mp4`, `.mov` |
| Imagens | `.jpg`, `.jpeg`, `.png` |
| Áudios | `.mp3`, `.wav`, `.m4a`, `.ogg` (incluindo OGG/Opus) |
| Legendas | `.srt` |

O suporte real dependerá também dos **codecs presentes nos arquivos**. A compatibilidade será validada com mídias reais, incluindo vídeos de iPhone e áudios de mensagens de voz.

## Inteligência artificial opcional

A integração com IA terá uma chave de ativação (*feature toggle*) **desligada por padrão**. Com a IA desativada, os recursos dependentes dela não aparecerão e nenhuma chamada aos provedores será realizada.

Ao ativá-la, o usuário poderá configurar um provedor compatível e sua **chave de API**, armazenada de forma protegida no Windows — nunca em texto simples no banco do projeto. A geração de legendas poderá abranger o vídeo inteiro ou apenas o trecho selecionado. Antes de enviar dados a serviços externos pela primeira vez, o aplicativo informará a operação e solicitará autorização.

A importação de SRT, a edição convencional e a exportação **não dependerão de IA**.

## Tecnologias e diretrizes técnicas

| Componente | Direção técnica |
|---|---|
| Plataforma | Windows (aplicativo desktop instalável) |
| Linguagem e interface | C#, .NET 8 ou superior e WPF |
| Processamento audiovisual | FFmpeg |
| Persistência dos projetos | SQLite |
| Acessibilidade | UI Automation, controles WPF e `AutomationPeer` em controles personalizados |
| Reprodução e prévia | **Em avaliação:** LibVLCSharp ou FFME |
| Instalador | **Em avaliação:** MSIX ou Inno Setup |

A definição da biblioteca de prévia, do empacotamento e de detalhes da integração com FFmpeg dependerá de provas de conceito, priorizando testes reais com NVDA.

## Estado e próximos passos

- [x] Refinamento funcional de importação, edição, áudio, elementos visuais, navegação e exportação (**RF01–RF06**).
- [x] Definição do gerenciamento de projetos com SQLite, salvamento e recuperação.
- [x] Definição dos requisitos de acessibilidade e navegação pelo teclado (**RNF01**).
- [ ] Validar as bibliotecas de reprodução e os fluxos de foco com NVDA.
- [ ] Implementar e testar os fluxos aprovados.
- [ ] Validar formatos/codecs e o comportamento das transições com arquivos reais.
- [ ] Realizar testes de usabilidade com pessoas cegas.
- [ ] Preparar a primeira distribuição instalável.

O **RF07** permanece reservado: seu conteúdo original não foi recuperado e não será definido por suposição.

## Especificação de requisitos

A referência funcional detalhada é a **Especificação de Requisitos de Software (ERS) v0.5**, de **18/10/2026**, incluindo requisitos identificados, critérios de aceitação, decisões técnicas e pendências. Ao versionar a documentação no repositório, o local sugerido é `docs/sonarFrame_ERS_v05.md`.

Este README apresenta a **visão do produto**; a ERS deve ser utilizada para orientar implementação, testes e mudanças de escopo.

## Identidade do produto

O nome **sonarFrame** combina a ideia de percepção por ondas sonoras com os quadros de um vídeo. A identidade visual ainda está em definição. O conceito proposto para o logotipo reúne uma moldura de vídeo e ondas de sonar, sem recorrer a símbolos estereotipados de deficiência visual.

A proposta de marca considera uma futura disponibilização comercial. A identidade visual, a disponibilidade do nome e as condições de distribuição ainda exigem validação.

## Licenciamento

**Licença ainda não definida.** A intenção de comercialização futura não implica, por si só, uma licença de código aberto. Antes de distribuir o aplicativo ou receber contribuições externas, serão formalizados os termos aplicáveis.
