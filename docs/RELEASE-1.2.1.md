# TechHub 1.2.1

## Melhorias

- Corrigido erro de binding reproduzido na tabela de Drivers.
- Consultas de Diagnósticos e Drivers com timeout, cancelamento, nova tentativa e proteção contra respostas tardias e comandos simultâneos.
- Tema exclusivamente escuro, com cores e estados de controles padronizados.
- Versão exibida a partir do executável e distinção entre atualizar dados e atualizar o aplicativo.

Validação: 141 testes aprovados, incluindo duas consultas reais somente leitura no Windows. Instalador por usuário exercitado com preservação do banco e configurações.

## Downloads

- **User Setup:** instalação no perfil do usuário, sem exigir administrador. Operações protegidas do Windows continuam exigindo privilégios apropriados.
- **Setup:** instalação para a máquina, com administrador.
- **Portable:** extraia o ZIP numa pasta gravável; preserve `Data` e `portable.flag` nas atualizações.

Confira SHA256SUMS.txt. Os executáveis ainda não possuem assinatura digital do editor; checksum não substitui assinatura.

## Limitações

Este lote não conclui todo o roteiro de evolução. A causa exata do crash originalmente relatado em Diagnósticos não foi confirmada por stack trace; os riscos de consulta e recuperação foram tratados e testados. Inspeção interativa em diferentes escalas, encerramento residual de versões antigas e melhorias do atualizador continuam pendentes.

Feche o aplicativo antes de instalar. Havendo versões por usuário e por máquina, use o atalho **TechHub (Usuário)** para abrir a instalação por usuário atualizada. Não há migração automática entre modalidades.
