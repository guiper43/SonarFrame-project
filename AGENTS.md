# Preferências de desenvolvimento

- Escrever código de domínio em português brasileiro, preservando os padrões existentes.
- Manter APIs, frameworks e termos técnicos estabelecidos em inglês.
- Não adicionar comentários ao código por padrão.

# Entregas para validação

- Publicar a versão de teste sempre em `artifacts/desktop` com `./run-desktop.ps1 -Publish`.
- Sobrescrever a publicação anterior; não criar pastas `desktop-<feature>` ou acumular versões em `artifacts`.
- Manter em `artifacts` somente a última publicação para validação. Amostras, relatórios, pacotes intermediários e laboratórios ficam em `.tools`.
- Se o aplicativo estiver aberto e impedir a substituição, solicitar que seja fechado; não encerrar o processo automaticamente e não criar outra pasta de versão.
- Não apagar projetos ou mídias do usuário ao limpar artefatos. Conferir os caminhos absolutos antes de excluir ou mover diretórios.
