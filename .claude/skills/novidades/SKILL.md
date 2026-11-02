---
name: novidades
description: Redige ou atualiza anúncios bilíngues (PT-BR e EN) das novidades do sonarFrame para grupos de WhatsApp e Telegram, salvando o texto em mk.txt na raiz do repositório. Use quando o usuário pedir divulgação de atualizações ou invocar novidades com recursos, notas de versão ou commits como referência.
---

# Novidades do sonarFrame

Produzir texto original de divulgação do sonarFrame, editor de vídeo não destrutivo para Windows, operável por teclado e voltado principalmente a pessoas cegas que usam NVDA. O resultado é um rascunho para os grupos do usuário; esta skill não autoriza enviar mensagens ou publicar o anúncio.

## Entrada e apuração

Usar a descrição do usuário como os assuntos a divulgar. Aceitar listas, texto livre, commits ou uma versão como referência. Consultar um mk.txt anterior apenas para entender formato e tom; escrever novas frases, sem copiar anúncios anteriores ou notas de versão.

Antes de anunciar cada recurso:

- Conferir o comportamento no código e nos testes relevantes. Usar CHANGELOG.md, docs/desktop.md e docs/release.md para contexto; docs/sonarFrame_ERS_v05.md descreve requisitos, não comprova implementação.
- Identificar a branch, os commits e a versão alvo. Distinguir implementação local, commit enviado ao remoto, integração na main e release publicada. Uma versão no projeto ou uma entrada em “Em desenvolvimento” não comprova disponibilidade pública.
- Se o usuário pedir uma campanha de lançamento sem indicar qual versão ou distribuição e isso não puder ser verificado, perguntar esse dado antes de afirmar que a atualização está disponível. Para uma prévia do desenvolvimento, usar linguagem explícita de teste ou desenvolvimento.
- Confirmar atalhos e nomes dos controles na versão anunciada. Explicar benefícios concretos para edição por teclado e leitor de tela, sem prometer homologação com NVDA apenas porque testes automatizados passaram.
- Distinguir prévia de uma mídia da biblioteca, reprodução da montagem, salvamento do projeto e exportação do vídeo. Não anunciar exportação, legendas, transições ou IA apenas por constarem do planejamento.
- Não importar referências à galeria de fotos, Android, Play Store, assinaturas ou planos de outro produto. Só mencionar preço ou licença comercial se houver informação confirmada para o sonarFrame.

## Texto e formato

Salvar em mk.txt na raiz do repositório, substituindo o anúncio anterior. Manter PT-BR primeiro e EN depois, com os mesmos fatos e limitações em linguagem natural. Se o usuário solicitar outro idioma, formato ou destino, seguir esse pedido.

Cada seção deve conter:

1. Identificação do idioma, título curto com sonarFrame e versão ou condição de teste quando confirmada.
2. Uma introdução breve, se ajudar a apresentar as novidades.
3. Um bloco por novidade: nome compreensível e uma a três frases sobre o que mudou e como isso ajuda a pessoa a editar. Evitar nomes internos de classes e detalhes de implementação.
4. Correções e estabilidade somente quando sustentadas pelas alterações verificadas. Não tratar uma falha intermitente conhecida como resolvida sem evidência.
5. Chamada final adequada à disponibilidade: baixar uma release, testar uma versão ou acompanhar o desenvolvimento.

Usar parágrafos curtos e separadores simples entre PT-BR e EN. Emojis são opcionais e moderados; nunca substituir palavras ou informações essenciais por símbolos. Evitar tabelas e excesso de decoração em mensagens para grupos.

Usar um link de download somente quando fornecido pelo usuário ou verificado para a versão anunciada. Sem release confirmada, pode-se apontar para https://github.com/guiper43/SonarFrame-project como repositório do projeto, sem descrevê-lo como download da nova versão. artifacts/desktop é a publicação local para validação, não um endereço público. Se orientar a distribuição portátil, lembrar que toda a pasta acompanha o executável.

## Entrega

Informar brevemente onde mk.txt foi salvo, quais novidades foram cobertas e quais arquivos ou commits sustentaram as afirmações. Sinalizar lacunas de versão, disponibilidade ou validação que afetem a divulgação. Não gerar tags, releases, commits ou mensagens externas como parte da redação.
