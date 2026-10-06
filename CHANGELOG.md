## [1.2.0.0] - 2026-10-06

Entrega referente ao item **#2366405** (Redmine), da análise #2366209 (NFS-e Nacional — DANFS-e —
Campos Fiscais Divergentes da NT 008 v1.02). A nota real nº 59 (`tpRetPisCofins=3`) expôs
convenções anteriores ao leiaute nacional vigente que ainda geravam descrições e totais
divergentes do DANFS-e oficial.

### Correções
- **4.1 — Descrição Contrib. Sociais - Retidas**: `tpRetPisCofins` deixa de ser interpretado pelo
  switch legado (1 a 4) e passa a usar a tabela vigente (1, 3, 4, 5, 6, 7, 8 e 9), exposta como
  API pública em `TpRetPisCofins` — fonte única, também consumida pelo conversor da NFS-e Nacional.
  O DANFS-e imprime `"{código} {descrição}"` (ex.: `3 PIS/COFINS/CSLL Retidos`); códigos fora da
  tabela (inclusive 0 e 2) exibem `-` com warning.
- **4.2 — Contribuições Sociais - Retidas / PIS / COFINS**: `vRetCSLL` é impresso como informado
  (CSLL isolada ou agregado, conforme o código), e PIS/COFINS (Débito Apuração Própria) exibem
  `vPis`/`vCofins` sem reentrar em nenhuma totalização.
- **4.3 — Total das Retenções**: reproduz `infNFSe/valores/vTotalRet` diretamente (`-` se ausente,
  sem aviso — a tag é opcional quando nada é retido), sem recomposição a partir de
  IRRF/CP/CSLL/PIS/COFINS/ISSQN.
- **4.4 — Exclusões e Reduções da Base de Cálculo**: passa a ser
  `vDescIncond + vCalcReeRepRes + vISSQN + vPis + vCofins` (ausentes = 0), em vez de só
  `vCalcReeRepRes`. A Base Após Exclusões continua reproduzindo `infNFSe/IBSCBS/valores/vBC`.
- **4.5 — Indicador de Operação**: célula única `cIndOp / cLocalidadeIncid / xLocalidadeIncid / UF`;
  `cIndOp` cai para `infDPS/IBSCBS/cIndOp` quando ausente em `infNFSe/IBSCBS`.
- **4.6 — Finalidade**: lida de `infDPS/IBSCBS/finNFSe` pela enumeração vigente (`0` = NFS-e
  regular); a enumeração antiga (1 a 4) deixa de ser interpretada. `IBSCBS.finNFSe` passa a `long?`
  para distinguir ausente de `0`.
- **4.7 — Data/hora**: `dhProc`/`dhEmi` passam a preservar o relógio do offset informado no XML
  (`DateTimeOffset`), sem conversão para o fuso do servidor/UTC — antes, em container UTC,
  `17:41:37-03:00` saía `20:41:37`. `InfNFSe.dhProc` passa a `string?`.
- **4.8 — Total Tributação Federal**: campo removido (não previsto na NT 008 v1.02 nem no Anexo I).

### Mudanças de API pública (por isso 1.2.0, e não patch)
- **Quebra de compatibilidade** em `NFSeSchema`: `InfNFSe.dhProc` `DateTime` → `string?` e
  `IBSCBS.finNFSe` `long` → `long?` (com `ShouldSerializefinNFSe`). Consumidores que leem esses
  campos precisam recompilar/ajustar.
- Nova classe pública `TpRetPisCofins` (classificação vigente de `tpRetPisCofins`).

### Diagnóstico
- Substituição de placeholders em passada única sobre o template: texto do XML no formato `{{...}}`
  (ex.: na descrição do serviço) não é mais reprocessado por substituições posteriores.
- `{{FED_TOTAL}}`, aposentado pela NT 008 v1.02, é removido de templates customizados
  (`DanfeOptions.TemplatePath`) com `TEMPLATE_PLACEHOLDER_EMPTY`; demais placeholders desconhecidos
  do consumidor são preservados.
- Indicador de Operação emite `NFSE_FIELD_MISSING` por componente ausente e
  `MUNICIPIO_NOT_FOUND` quando `cLocalidadeIncid` não existe na tabela do IBGE (ou não é numérico),
  ou um aviso próprio quando a tabela do IBGE não foi inicializada;
  quando `xLocalidadeIncid` já traz "Município - UF", a UF não é repetida.
- `finNFSe` ausente (em nota com grupo IBS/CBS) ou fora da enumeração vigente gera aviso;
  `dhProc`/`dhEmi`/`dCompet` ausentes geram um único aviso (antes, dois) e `dhProc`/`dhEmi` presentes
  mas não reconhecidos como data/hora, um aviso próprio. A competência passa a ser formatada com
  cultura invariante (sempre `dd/MM/yyyy`).
- `infNFSe/cLocIncid` com espaços ou não numérico deixa de derrubar o `Render` (antes,
  `FormatException`) e o aviso de ausência passa a olhar o próprio `cLocIncid` (antes, por engano,
  `cLocPrestacao`).

### Testes
- Nova fixture sintética `nfse-caso-2366209.xml` (valores fiscais da nota nº 59, sem dados reais) e
  `Nt008CamposFiscaisTests`/`TpRetPisCofinsTests` cobrindo 4.1 a 4.8 (todos os códigos de
  `tpRetPisCofins`, cada parcela da fórmula de Exclusões, offsets `-05:00`/`+02:00`/`Z`).
- Snapshots dourados regravados; a diferença se limita aos campos dos itens 4.1 a 4.8.

### Pontos de atenção (5.2 — não alterados)
- `GetDescricaoRegimeEspecial` (`regEspTrib` 0 a 6) e `GetDescricaoAmbienteGerador` (`ambGer`)
  continuam baseados em documentação pública, não na NT 008; confirmar contra o XSD vigente.

## [1.1.3.0] - 2026-08-06

Entrega referente ao item **#2358429** (Redmine) — correção de packaging descoberta ao investigar
por que consumidores via `PackageReference` (ex.: `nfsenacional`) não recebiam os ajustes de
layout das versões 1.1.1/1.1.2.

### Correções
- **Correção de bug crítico de empacotamento**: `DanfeHtmlRenderer` resolve o template em
  `{AppContext.BaseDirectory}/Assets/Templates/Danfe.html` (caminho "plano"). Para consumidores
  via `PackageReference`, o `Content` dos `Assets` era empacotado em
  `contentFiles/any/any/Nogueira.NFSe.Danfe.FixLinux/Assets/...` — com o nome do pacote como
  subpasta —, então o NuGet copiava para
  `{bin}/Nogueira.NFSe.Danfe.FixLinux/Assets/Templates/Danfe.html` no projeto consumidor, **não**
  no caminho plano esperado pelo renderizador. Isso nunca funcionou automaticamente para esse tipo
  de consumo; o projeto `nfsenacional` contornava manualmente mantendo uma cópia local de
  `Assets/` (incluindo `Danfe.html`) dentro do próprio repositório — cópia que parou de ser
  atualizada em 30/07/2026 e, por isso, sombreava silenciosamente todos os ajustes de layout
  publicados nas versões 1.1.1 e 1.1.2 (o app sempre renderizava a partir da cópia antiga, nunca
  do template atualizado da biblioteca).
- Corrigido o `PackagePath` do `Content Include="Assets\**\*"` em `Danfe.csproj` para
  `contentFiles/any/any/Assets` (sem a subpasta do nome do pacote), fazendo o NuGet copiar para o
  mesmo caminho plano que consumidores por referência de projeto (`GeraDanfe`, `Danfe.Tests`) já
  recebem — eliminando a necessidade de qualquer cópia local nos projetos consumidores.
- Verificado manualmente: `dotnet pack` + `dotnet restore` num consumidor de teste confirmam que
  `Assets/Templates/Danfe.html` chega no caminho correto, com o conteúdo atualizado (margens/
  `line-height` da versão 1.1.2), sem exigir nenhuma configuração ou cópia local do consumidor.

## [1.1.2.0] - 2026-08-06

Entrega referente ao item **#2358429** (Redmine) — feedback de revisão sobre #2357202, da análise
#2356243 (NFS-e Nacional — Disponibilidade Local e Conformidade ao Novo Layout).

### Correções
- **Correção de bug (regra 4.6.3)**: Benefício Municipal e País Resultado da Prestação do Serviço
  nunca eram renderizados mesmo com `tpBM` e `cPaisResult` preenchidos no XML — o campo Benefício
  Municipal lia `infDPS.valores.trib.tribMun.BM.nBM` (grupo errado) em vez de
  `infNFSe.valores.tpBM`, e o campo País Resultado lia o endereço no exterior do tomador
  (`infDPS.toma.end.endExt.cPais`) em vez de `infDPS.valores.trib.tribMun.cPaisResult`.
- **Correção de bug residual (regra 4.8.6)**: um caso específico — NFS-e Substituída com todos os
  blocos opcionais preenchidos e a linha adicional "NFS-e Subst.:" nas informações complementares —
  continuava saindo em 2 páginas mesmo após o ajuste de espaçamento da versão 1.1.1. Causa raiz
  dupla: (1) diferenças de fonte entre ambientes (Windows/Linux) consomem a margem de segurança de
  forma imprevisível; (2) a própria medição de altura usada nos testes/geração usava a largura
  padrão do Puppeteer (800px) em vez da largura real de impressão (~779px, A4 menos margens),
  subestimando a quebra de linha real. Corrigido ajustando o viewport da página à largura real de
  impressão antes de medir, e adicionado encolhimento automático da impressão
  (`DanfePdfGenerator.ComputeScaleToFitOnePage`) sempre que o conteúdo medido exceder uma página A4
  — limitado a um piso de 8% de encolhimento para preservar os tamanhos mínimos de fonte do Anexo I
  da NT-008. Substitui o ajuste manual de espaçamento por um mecanismo durável que se auto-corrige
  para combinações de campos futuras, em vez de reagir caso a caso.
- Adicionado teste de regressão para o cenário residual (`Substituida_ComReferenciaEBlocosOpcionais_GeraPdfValido`)
  e testes unitários puros para `ComputeScaleToFitOnePage`.

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
