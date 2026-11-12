# Ferramenta de leitura de mídias

Esta distribuição de desenvolvimento inclui `ffprobe.exe` e `ffmpeg.exe`, do FFmpeg 9.0.2, executados como processos separados para ler metadados e preparar a prévia local da sequência principal. Não há envio de mídias à internet. O aplicativo não baixa dependências durante a importação ou reprodução.

Origem do pacote Windows: https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip

SHA-256 do arquivo original: `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`

O fornecedor é indicado na página https://ffmpeg.org/download.html. A compilação utilizada tem licença GPLv3; a licença e o README originais acompanham o executável. Referência do código-fonte da compilação: https://github.com/FFmpeg/FFmpeg/commit/946fcce07b . Informações de compilação e bibliotecas: https://www.gyan.dev/ffmpeg/builds/ . Esta preparação local não define a licença do SonarFrame nem constitui publicação de um instalador; uma distribuição externa deverá preservar e atender às condições das dependências incluídas.

Para reproduzir a preparação local, execute `./setup-media.ps1`. O script baixa a versão fixada e verifica o SHA-256. `run-desktop.ps1` chama esse preparo apenas quando o FFprobe local está ausente. Os binários e o arquivo baixado ficam em `.tools`, ignorado pelo Git. A distribuição local do Desktop copia `ffprobe.exe` e `ffmpeg.exe` para `tools/`, acompanhados da licença e do README originais.

A leitura usa argumentos separados, sem shell, protocolo de arquivo local, limite de saída e timeout de 30 segundos por arquivo. Cancelamento encerra o processo de leitura. A detecção de codecs e duração não representa uma validação de decodificação de todos os quadros do arquivo. Uma divergência entre taxas média e nominal sinaliza possível VFR; não é uma análise completa dos timestamps. Navegação VFR quadro a quadro permanece pendente.

## Prévia e reprodução em desenvolvimento

O Desktop inclui `LibVLCSharp.WPF` 3.10.2 e `VideoLAN.LibVLC.Windows` 3.0.24, restaurados do NuGet com versões e hashes registrados em `packages.lock.json`. O runtime nativo Windows x64 acompanha a distribuição em `libvlc/win-x64`; não exige VLC instalado nem baixa componentes durante a reprodução. Os metadados dos pacotes declaram LGPL-2.1-or-later. Fontes e informações dos projetos: [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) e [pacotes LibVLC](https://code.videolan.org/videolan/libvlc-nuget).

O motor é inicializado sob demanda em thread de trabalho; imagens estáticas da biblioteca usam WPF e não carregam LibVLC. A prévia da biblioteca usa os arquivos originais locais; a reprodução da sequência principal prepara um arquivo temporário local com `ffmpeg.exe` antes de abrir no motor. Camadas e exportação final continuam fora desta etapa.
