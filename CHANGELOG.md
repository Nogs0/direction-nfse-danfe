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
