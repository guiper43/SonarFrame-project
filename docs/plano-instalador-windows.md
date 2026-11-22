# Plano: instalador Windows acessível

Base: `develop`, commit `676b81c`. Branch: `feature/instalador-windows`, em worktree isolado da feature de propriedades visuais.

## Objetivo e escopo

Entregar um instalador Inno Setup por usuário para Windows x64, com runtime .NET, FFmpeg e LibVLC incluídos, assistente padrão em português brasileiro, atalhos no Menu Iniciar e na Área de Trabalho, desinstalador e associação opcional de `.sonarframe`. A associação deve abrir o projeto e preservar a confirmação de recuperação existente.

O instalador não encerra o editor automaticamente, não remove projetos, mídias ou configurações e não força a substituição da escolha de aplicativo padrão feita no Windows. Assinatura Authenticode, atualização automática e validação falada com NVDA/máquina limpa ficam fora da implementação automatizada.

## Rastreabilidade

| Referência | Entrega e verificação |
|---|---|
| DT01 | Aplicativo instalável; decisão por Inno Setup conforme solicitação do usuário |
| RNF01.01, RNF01.03, RNF01.13, RNF01.18–21 | Assistente nativo, controles padrão e cancelamento; roteiro de aceite por teclado/NVDA |
| RNF01.14, RNF01.16 | Erros de abertura por associação anunciados no editor e preservados nas mensagens recentes |
| GP08 e recuperação | Reuso de `AbrirCaminhoAsync`, sem alterar o formato do projeto ou aplicar recuperação silenciosa |

## Decisões por camada

- Core, Application e Infrastructure: sem novas dependências ou alterações de domínio.
- Desktop: entrada por argumento `.sonarframe` em arquivo parcial próprio, reaproveitando a abertura existente; mutex apenas para detectar uso durante instalação/desinstalação, sem impedir várias janelas.
- Distribuição: script `.iss`, compilador oficial fixado com SHA-256 em modo portátil em `.tools`, comando de geração, testes de instalação/atualização/desinstalação isolados e integração do EXE/checksum no pacote de release.
- Publicação: `./run-desktop.ps1 -Publish` gera `artifacts/desktop` deste worktree. Instaladores e relatórios ficam em `.tools`; a publicação da pasta do Antigravity não será sobrescrita.

## Incrementos verificáveis

1. Abertura de projeto por argumento, com testes STA de caminho com espaços/acentos, argumentos inválidos, arquivo ausente e projeto corrompido.
2. Compilador portátil, instalador e script de geração que verificam versão e dependências antes do empacotamento.
3. Testes reais usando identidade de instalação, extensão e atalhos exclusivos: instalar, conferir payload/atalhos/registro, atualizar, bloquear atualização com aplicativo em uso e desinstalar preservando arquivos do usuário.
4. Build e suíte completa, publicação, compilação do instalador, revisão `code-reviewer`, documentação e auditoria final.

## Auditoria final

- `build.ps1`: compilação sem avisos/erros; 49 testes Desktop e 112 de domínio/infraestrutura aprovados. Os 8 testes nativos/benchmark optativos ficaram desabilitados; não houve alteração de reprodução ou formatos.
- Os testes usam arquivos sob o diretório do assembly. Para evitar o limite de caminhos do SQLite na worktree aninhada, a validação local usou um alias temporário de unidade para a mesma pasta, sem alterar os testes existentes.
- `run-desktop.ps1 -Publish`: publicação self-contained em `artifacts/desktop` desta worktree; compilação do instalador concluída.
- `testar-instalador.ps1 -SkipPublish`: aprovado o ciclo real de instalação sem/com associação, hashes do payload, atalhos, atualização e desinstalação preservando projetos/mídias de teste e o aplicativo padrão anterior. Bloqueio de desinstalação com mutex ativo também conferido separadamente.
- Revisão `code-reviewer`: corrigidos foco após mostrar o editor, verificação da versão do compilador e reconhecimento dos PNGs distribuídos pelo LibVLC. Sem achados relevantes pendentes no diff final; sem novas dependências NuGet, alterações de domínio ou comentários no código.
- Revisão `desktop-accessible-ui`: abertura usa os controles, anúncios e fluxo de recuperação existentes; foco de entrada coberto por teste STA. O instalador usa o assistente padrão, sem controles customizados.
- Aceite falado com NVDA e execução em Windows limpo ainda exigem homologação manual conforme `docs/instalador.md`. Instalador local sem assinatura digital.
