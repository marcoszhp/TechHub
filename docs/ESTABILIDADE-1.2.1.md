# TechHub 1.2.1 — lote de estabilidade e tema escuro

Verificado em 14/10/2026. Implementação incremental das etapas A/B/C do prompt mestre; não significa conclusão de todas as etapas.

## Evidência e causa

- Encontradas duas instalações locais 1.1.0: por usuário e por máquina. A inspeção inicial restrita não retornou processos; uma inspeção posterior fora da restrição confirmou o aplicativo aberto em `C:\Program Files\TechHub\TechHub.App.exe`, respondendo. A versão em execução é a antiga 1.1.0; coexistência e migração permanecem na etapa D.
- Reproduzida a exceção WPF ao associar `CheckBox.IsChecked` com modo padrão à propriedade calculada, somente leitura, `DriverInfo.HasProblem`. O modo padrão permite escrita. A coluna real agora usa `Mode=OneWay`; a tela compilada renderiza com dispositivos com/sem problema.
- Não foi recuperada uma stack trace do crash original de Diagnósticos. Sua causa exata permanece não confirmada. Foram corrigidos riscos verificados de espera sem limite, cancelamento síncrono do PowerShell e falha marcada como módulo carregado.
- Fundos claros residuais e bordas clássicas foram encontrados no XAML e nas renderizações; os controles agora compartilham recursos escuros.

## Correções

- Consultas em segundo plano, timeout de 30 segundos, cancelamento sem bloquear callbacks na UI, preservação de dados anteriores e nova tentativa.
- No máximo duas consultas ainda encerrando por módulo: uma consulta abandonada e uma tentativa nova. Uma terceira recebe aviso explícito; consultas travadas não são acumuladas indefinidamente.
- Resultados tardios não substituem a consulta nova. Falhas e resultados parciais não entram no cache de carregamento bem-sucedido.
- Consultas, reparos, instalação e backup bloqueiam comandos concorrentes. Reparos não oferecem cancelamento que não possa ser garantido.
- Bindings somente leitura, paleta escura, tabelas, seleção, foco, combos, formulários e botões padronizados; versão derivada do assembly.
- Falhas previstas permanecem no módulo; exceção inesperada global é registrada e encerra com aviso, sem afirmar que a aplicação pode continuar em estado desconhecido.

## Verificação executada

- Restore com acesso NuGet: sem avisos de segurança reportados.
- Suíte existente: 108 testes aprovados.
- Nova suíte WPF/VM: 33 testes aprovados, incluindo duas consultas reais somente leitura de Diagnósticos e Drivers neste computador.
- Regressões: falha/retry, parcial, timeout, cancelamento cooperativo/não cooperativo, resultado e erro tardios, limite de consultas, callback lento, prefixo síncrono, bloqueio entre comandos e cache após falha.
- XAML real renderizado em memória com dados sintéticos, dimensões equivalentes a janelas de 1080 e 1360 pixels. Verificados limites horizontais dos botões. Capturas em `artifacts/ui-validation-final`.
- Nenhum SFC, DISM, backup/instalação de driver ou varredura de rede foi executado para testar.
- Publicação autocontida win-x64 e dois instaladores Inno Setup compilados. Executável local identificado como 1.2.1.0. ZIP e hashes gerados em `artifacts/release-1.2.1/github-assets`.
- A primeira tentativa foi bloqueada pelo aplicativo aberto. Após confirmação do usuário de que salvou/fechou, foi encerrado o processo antigo residual sem janela. Instalador por usuário concluído (saída0), banco/configurações preservados por comparação de hashes e backup privado em `artifacts/local-user-backup-before-1.2.1/Data`.
- Aplicação instalada1.2.1.0 aberta pelo caminho do usuário; janela respondendo e log de inicialização confirmados. Atalho “TechHub (Usuário)” conferido. Instalação antiga de máquina não removida. Publicação remota deste lote ainda pendente.

## Limites e próximos passos

- As capturas não são inspeção manual do aplicativo instalado. Arraste, controles de janela, leitores de tela e escalas 125/150/200% ainda precisam de validação interativa.
- O tratamento completo de coleta parcial e do Event Viewer sem eventos correspondentes ainda precisa de regressão específica; não foi causa confirmada do relato original.
- Etapa D: atualizar com integridade/autenticidade, modalidade correta e preservação de dados; revisar coexistência usuário/máquina/portátil. Não há certificado de assinatura do editor.
- Depois: etapas E/F/G (rede, módulos, Qwen) e H (publicação/automação), conforme o prompt mestre. Qwen não foi utilizado neste lote.
- Recuperação: reverter o commit deste lote e reconstruir. Não houve alteração de esquema nem migração de dados.

## Decisão técnica

Decisão TH-STABILITY-01, revisão 1, escopo deste lote: manter WPF/MVVM e serviços existentes, corrigir bindings e delimitar consultas sem alterar contratos de manutenção. Classes 2/3 dentro da autorização explícita do usuário para crashes e tema escuro. Método validado por testes locais em .NET 8/WPF; reabrir se os testes ou o comportamento instalado divergirem. Não substitui a validação de instalação/atualização da etapa D.
