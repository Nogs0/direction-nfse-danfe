## [1.1.1.0] - 2026-08-05

Entrega referente ao item **#2357949** (Redmine) — feedback de revisão sobre #2357201 e #2357225,
da análise #2356243 (NFS-e Nacional — Disponibilidade Local e Conformidade ao Novo Layout).

### Correções
- **Correção de bug (regra 4.8.6)**: massa completa com todos os blocos opcionais preenchidos
  (tomador, intermediário, obra, evento, informações complementares, IBS/CBS) mais descrição de
  serviço longa e o canhoto habilitado saía em 2 páginas A4 — a 2ª contendo apenas o bloco
  CANHOTO. Espaçamento vertical (margens/paddings redundantes e `line-height`) do template e do
  renderizador ajustado para caber em uma única página A4, sem cortar conteúdo obrigatório.
- Adicionado teste de regressão automatizado (`MassaCompleta_ComCanhoto_CabeEmUmaUnicaPaginaA4`)
  cobrindo os cenários regular, cancelada, substituída e Produção Restrita com o canhoto
  habilitado.

## [1.1.0.0] - 2026-07-30

Entrega referente ao item **#2357201** (Redmine) — 4.2 e 4.4 a 4.8 da análise #2356243
(NFS-e Nacional — Disponibilidade Local e Conformidade ao Novo Layout).

### 4.2 — Verificação e evolução da biblioteca de geração
- Verificado o pacote interno (`Nogueira.NFSe.Danfe.FixLinux` 1.0.13.0, este fork) e a
  biblioteca pública de origem: não foi localizada versão pública do `Direction.NFSe.Danfe`
  nem de nenhum outro pacote .NET com suporte integral ao DANFSe v2.0 (IBS/CBS, destinatário,
  intermediário) definido pela Nota Técnica nº 008 v1.02.
- Decisão: evoluir este fork/renderizador local (regra 4.2.4), sem aguardar publicação externa.
- Versão adotada com as correções abaixo: `1.1.0.0` (este commit).

### 4.4 a 4.8 — Layout DANFSe v2.0 (Nota Técnica nº 008 v1.02)
- Cabeçalho atualizado para o modelo "DANFSe v2.0", com município/ambiente gerador/tipo de
  ambiente do emitente.
- Adicionados os blocos de **Destinatário da Operação** e **Intermediário da Operação**, com
  as regras de supressão/substituição da Nota Técnica (participante não identificado,
  destinatário igual ao tomador).
- Adicionado o bloco completo de **Tributação IBS/CBS**, suprimido por completo quando o grupo
  não existir no XML; novos totais de IBS, CBS e valor líquido acrescido de IBS/CBS.
- Bloco de Tributação Municipal (ISSQN) agora é substituído pela indicação de operação não
  sujeita ao ISSQN quando não houver incidência.
- Informações complementares reorganizadas conforme a Nota Técnica, incluindo obra, imóvel,
  evento, documento referenciado, pedido/item de pedido e a linha obrigatória de Totais
  Aproximados dos Tributos (Lei nº 12.741/2012), preferindo os valores monetários ou percentuais
  conforme o que estiver informado no XML — nunca recalculado.
- Adicionado bloco opcional de Canhoto (`DanfeOptions.ExibirCanhoto`, padrão `true`).
- **Correção de bug**: o grupo de Intermediário da Operação (`infDPS/interm`) nunca era
  deserializado — a classe/propriedade estavam nomeadas incorretamente (`longermediario`/
  `longerm`, resultado de uma substituição de texto anterior que também afetou a palavra
  "Interm..."). Corrigido para `Intermediario`/`interm` com o mapeamento XML explícito.
- Removido do cabeçalho o recurso de logo por município (`PREFEITURA_LOGO`/`LOGO_NAME`): o
  modelo oficial do DANFSe v2.0 não prevê esse elemento visual no cabeçalho.

## [1.0.13.0] - 2026-07-30

Entrega referente ao item **#2357225** (Redmine) — 4.3 Estabilidade do Gerador Local em Uso
Contínuo, da análise #2356243 (NFS-e Nacional — Disponibilidade Local e Conformidade ao Novo
Layout).

### 4.3 — Estabilidade do gerador local em uso contínuo
- Corrigido `DanfePdfGenerator`: página com falha de renderização (timeout, erro de conversão,
  processo interno derrubado) deixa de ser devolvida ao pool e passa a ser descartada.
- O pool de páginas é drenado antes de qualquer relançamento do navegador interno, evitando
  páginas órfãs de execuções anteriores em circulação.
- O pool nunca mais cresce acima do tamanho configurado.
- Removida a flag `--single-process` do Chromium (acoplava falha de renderização à queda do
  navegador inteiro).
- Adicionada observabilidade interna opcional via `ILogger<DanfePdfGenerator>`.
- Versão adotada com as correções acima: `1.0.13.0` (este commit).

## [0.1.7] - 2026-01-12

### Added
- Atualizando o schema da NFSe para a implementação disponível no ambiente nacional no dia 16/12/25. Obrigado a @Threads-creator
- Preenchendo CPF onde poderia ser preenchido (tomador, prestador e afins). Obrigado a @Threads-creator
- Preenchendo totalizadores de impostos federais no DANFSe e demais campos não preenchidos originalmente. Obrigado a @Threads-creator
- Ajustado negrito no layout. Obrigado a @ludero

## [0.1.5] - 2026-01-08

### Bugfix
- Exibição correta para casos de NFSe Canceladas

## [0.1.4] - 2026-01-07

### Added
- Opção para imprimir a DANFE com a tarja de Cancelada
- Inserido a logo de dados de Agudos, SP. Obrigado a @ludero
- Melhora no layout da DANFE. Obrigado a @ludero
- Melhorado e complementado o readme incluindo informações de como gerar a Tarja de Cancelada e as logos dos municípios

## [0.1.0] - 2025-12-15

### Added
- Geração de DANFSe a partir de XML NFSe Nacional
- Renderização HTML desacoplada
- Geração de PDF
- Warnings padronizados
- Golden tests de HTML
