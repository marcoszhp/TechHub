# D1 — atualização integrada (candidata 1.3.0)

## Contrato de execução — 14/10/2026

Objetivo autorizado: continuar o prompt mestre, primeiro atualização pelo próprio app. Fonte: fase D de `PROMPT-MESTRE-EVOLUCAO-TECHHUB.md`; base limpa `59007de`, versão publicada 1.2.1. Esta entrega não encerra toda a fase D.

Método fechado: manter .NET 8/WPF, Inno Setup e GitHub Releases; auxiliar .NET autocontido, sem serviço residente, sem novo fornecedor ou biblioteca. Skill: implement-controlled-software-changes. Coordenador: arquitetura, ciclo de vida, segurança, integração; executores Sol/medium com arquivos separados para transporte e interface; revisão independente antes de aprovação. Discrição técnica classe 2/3, sem mudanças de produto não aprovadas.

Escopo: versões instaladas win-x64, usuário e máquina, mantendo modalidade e diretório. Usuário não recebe pedido de administrador; máquina conserva UAC do instalador. Portátil/desconhecido fica explicitamente manual, sem converter instalação. Verificação manual, sem instalar automaticamente. Atualizações periódicas, substituição portátil, assinatura de código e rollback automático completo ficam para D2. A primeira migração da 1.2.1 exige instalar a versão que contém o novo atualizador.

Fluxo: consultar release estável do repositório fixo; selecionar asset exato por modalidade; exigir tamanho e digest SHA256 da API HTTPS; download cancelável com progresso em pasta exclusiva e arquivo parcial; verificar tamanho/hash; consentimento para salvar trabalho; bloquear se há ações ativas; snapshot SQLite/JSON com serviço existente; auxiliar copiado para staging; aguardar saída do processo original sem forçar encerramento; revalidar pacote; executar somente Inno com argumentos fixos; confirmar versão instalada; reiniciar sem elevar o processo principal; registrar resultado para próxima abertura.

Fronteiras: nunca executar pacote parcial/não verificado, comandos remotos genéricos, manutenção Windows, migrar banco, apagar Data, encerrar outros processos, incluir backups privados em releases ou alegar assinatura do fabricante. HTTPS e digest GitHub protegem transporte/integridade sob confiança na conta/repositório, não substituem Authenticode. Backup não equivale a rollback transacional de binários e banco; falha mantém logs, backup e instrução de recuperação, sem restaurar banco automaticamente.

Assurance HIGH_RISK: testes determinísticos (metadados, modo, hash, truncamento, cancelamento, limites, caminhos, falha/timeout do helper), testes de comandos/estado WPF, build, cenário de duas versões isolado sem tocar nos dados reais e revisão independente. Oráculos: arquivos/hash/versão real e resultado de processo, não apenas mensagens da UI. Publicação bloqueada se teste crítico falhar; não declarar reinício real validado apenas com mocks. Ambiente: Windows local, SDK 8.0.425 já disponível, Inno Setup 6; testes fora do sandbox quando sockets exigirem. Rede só GitHub e distribuição configurada. Proibido alterar fontes sincronizadas ou arquivos alheios.

Recuperação: alterações em Git revisáveis; preservar versão pública 1.2.1 até aceite. Pacote anterior disponível para reparação manual, sem migração de schema nesta entrega. Resultado: código, testes, evidências e pendências aqui e no PROJECT_CACHE. Limite de uso deve ser verificado entre fases grandes; adiar próxima fase pesada a <=5% restante.

Referências verificadas: [Inno: parâmetros](https://jrsoftware.org/ishelp/topic_setupcmdline.htm), [Inno: aplicações em uso](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm), [GitHub: digest dos assets](https://docs.github.com/en/rest/releases/assets). Não usar `/FORCECLOSEAPPLICATIONS`; fechar/reiniciar somente com consentimento e validação de identidade.

## Checkpoint — implementação local, não liberada

230 testes passaram (177 Core/Infra + 53 UI), incluindo 89 novos; os 90 testes selecionados por `Update` passaram novamente após reforço dos guards. Build do auxiliar aprovado sem avisos/erros. Restore dos demais projetos apresentou NU1900 por indisponibilidade da consulta de vulnerabilidades: auditoria de dependências NÃO confirmada.

Revisão independente encontrou e orientou duas correções: deadline não cancela conclusão após iniciar instalador; mutex é descartado também em falhas. O teste de deadline passou. Revisor confirmou a lógica posteriormente pelo relato; revisão final do patch e E2E ainda necessários. A proteção por handles dos diretórios foi estendida do download à preparação e execução.

Pendências bloqueantes de release: gerar/distribuir auxiliar autocontido; compilar os instaladores; validar fluxo real entre duas versões, consentimento/UAC, arquivo mantido aberto durante Inno, encerramento do processo, preservação/backup e reinício. Os testes com runtime falso não provam esses efeitos no Windows instalado. Não houve alteração do aplicativo instalado, GitHub, R2 ou Pages nesta rodada. A instalação personalizada não é suportada em D1, assim como portátil.

Regra preventiva atingida: 97% da janela principal consumida (3% restante). Continuidade salva no PROJECT_CACHE; próxima etapa pesada adiada, sem compra de créditos nem uso de resets.

## Retomada e aceite técnico delimitado — 14/10/2026, 23:32

- Janela renovada observada pela ferramenta; nenhum crédito/reset foi acionado pelo agente. Restore forçado com auditoria habilitada concluiu sem NU1900. SDK 8.0.425 e Inno 6.7.3.
- Suíte atual: 180 testes Core/Infra +63 UI/helper =243 aprovados; registros `artifacts/test-results-1.3.0`.
- Integração real isolada: `scripts/Test-UpdateIntegration.ps1`, evidência `artifacts/update-integration/6c3895fe8851496c89ded58ef268054f/evidence.json`. Inno substituiu executáveis de teste1.2.1 por1.3.0, mantendo o pacote aberto sem compartilhamento de escrita; processo pai real saiu normalmente, backup SQLite/JSON conferido, hashes dos dados preservados, reinício no mesmo SID sem elevação. A fixture substitui a consulta GitHub e a política do ponto de entrada, não é teste de clique completo no app instalado.
- Confiança verificada separadamente contra a release pública1.2.1: metadados oficiais aceitos, digest alterado recusado, nenhum pacote executado. Evidência em `artifacts/update-integration/trust-check/trust-evidence.json`.
- App WPF de produção instanciado em processo de teste, dados isolados: fechamento normal em2557ms e caminho de atualização em2460ms, saída0, sem kill, banco/auditoria confirmados. `artifacts/lifecycle/24c1782235a1427488e4b06b5fe01282/process-results.json`. Uma falha anterior era da fixture atribuindo ResourceAssembly após definição; removida, sem alteração de comportamento de produção para contorná-la.
- Revisão independente Sol/medium confirmou diretamente correções de deadline/descarte e encontrou duas condições adicionais. Corrigidas e rerevisadas: tentativa abandonada grava `aborted`, validada antes do mutex; máquina usa marcadores globais com ACL AU–SYNCHRONIZE e rechecagem de startup, Inno consulta Local+GlobalRunning. Testes determinísticos cobrem o retry e vida útil dos marcadores. Helper HTTPS sem redirecionamentos e com limite de1MiB.
- Resultados anteriores do atualizador não são lidos no perfil instalado quando o app está portátil. Confirmação final de reinício depois do download protege trabalho que possa ter sido editado nesse intervalo.
- Aceite: código revisado, componentes reais críticos exercitados e pacote candidato pode ser gerado. Não declarar homologação universal: duas contas/RDS, UAC com outro SID e o clique completo consentimento→auxiliar em uma instalação já distribuída ainda não foram exercitados. Esses limites devem acompanhar a release; D2 permanece pendente.
